import base64
import gc
import importlib.util
import json
import os
import re
import sys
import traceback
from pathlib import Path
from types import MethodType, ModuleType


if importlib.util.find_spec('torchaudio') is None:
    torchaudio = ModuleType('torchaudio')
    torchaudio.functional = ModuleType('torchaudio.functional')
    torchaudio.transforms = ModuleType('torchaudio.transforms')
    sys.modules['torchaudio'] = torchaudio
    sys.modules['torchaudio.functional'] = torchaudio.functional
    sys.modules['torchaudio.transforms'] = torchaudio.transforms

import torch
import torchvision
from deepspeed import comm as dist
from diffusers import FlowMatchEulerDiscreteScheduler
from torch import nn
from tqdm import tqdm

from utils.common import AUTOCAST_DTYPE, is_main_process
from utils.dataset import DatasetManager
from utils.saver import Saver


HOOK_VERSION = 2
DEFAULT_CONFIG = {
    'enabled': True,
    'every_n_epochs': 1,
    'positive_prompt': 'a golden retriever running through a grassy field',
    'negative_prompt': '',
    'sampler': 'euler',
    'scheduler': 'normal',
    'width': 1024,
    'height': 1024,
    'steps': 20,
    'cfg': 5.0,
    'seed': 42,
    'offload_pipeline_for_vae': True,
}


def _log_vram(stage):
    free, total = torch.cuda.mem_get_info()
    allocated = torch.cuda.memory_allocated()
    reserved = torch.cuda.memory_reserved()
    peak = torch.cuda.max_memory_allocated()
    gib = 1024 ** 3
    print(
        f'[Anima preview VRAM] {stage}: '
        f'allocated={allocated/gib:.2f} GiB, reserved={reserved/gib:.2f} GiB, '
        f'peak={peak/gib:.2f} GiB, device_used={(total-free)/gib:.2f}/{total/gib:.2f} GiB'
    )


def _is_anima(model):
    return model.config.get('model', {}).get('type') == 'anima'


def _as_bool(value):
    if isinstance(value, str):
        return value.strip().lower() not in {'0', 'false', 'no', 'off', ''}
    return bool(value)


def _environment_config():
    encoded = (
        os.environ.get('ANIMA_PREVIEW_CONFIG_B64')
        or os.environ.get('DIFFPIPEFORGE_PREVIEW_CONFIG_B64')
    )
    raw = (
        os.environ.get('ANIMA_PREVIEW_CONFIG_JSON')
        or os.environ.get('DIFFPIPEFORGE_PREVIEW_CONFIG')
    )
    if encoded:
        raw = base64.b64decode(encoded).decode('utf-8')
    if not raw:
        return {}
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise TypeError('preview environment config must be a JSON object')
    return value


def _preview_config(model):
    config = dict(DEFAULT_CONFIG)
    toml_config = model.config.get('preview', {})
    if isinstance(toml_config, dict):
        config.update(toml_config)
    config.update(_environment_config())
    if 'prompt' in config:
        config['positive_prompt'] = config['prompt']
    config['enabled'] = _as_bool(config['enabled'])
    config['every_n_epochs'] = max(1, int(config['every_n_epochs']))
    config['width'] = max(64, int(config['width']) // 16 * 16)
    config['height'] = max(64, int(config['height']) // 16 * 16)
    config['steps'] = max(1, int(config['steps']))
    config['cfg'] = float(config['cfg'])
    config['seed'] = int(config['seed'])
    config['positive_prompt'] = str(config['positive_prompt']).strip()
    config['negative_prompt'] = str(config['negative_prompt'])
    config['sampler'] = str(config['sampler']).lower()
    config['scheduler'] = str(config['scheduler']).lower()
    config['offload_pipeline_for_vae'] = _as_bool(config['offload_pipeline_for_vae'])
    return config


def _get_conds(self, inputs):
    return inputs['prompt_embeds'], inputs['attn_mask'], inputs['t5_input_ids'], inputs['t5_attn_mask']


def _vae_decode(self, latents):
    p = next(self.vae.model.parameters())
    decoded = self.vae.model.decode(latents.to(p.device, p.dtype), self.vae.scale)
    decoded = ((decoded.float() + 1) / 2).clamp_(0, 1)
    return decoded.movedim(1, -1)


def _ensure_sampling_adapters(model):
    if getattr(model, '_local_epoch_preview_prepared', False):
        return
    model.is_video_vae = True
    model.get_conds = MethodType(_get_conds, model)
    model.vae_decode = MethodType(_vae_decode, model)
    model._local_epoch_preview_prepared = True


def _move_aux_models_to_cpu(model):
    for text_encoder in model.get_text_encoders():
        if isinstance(text_encoder, nn.Module) and next(text_encoder.parameters()).device.type != 'meta':
            text_encoder.to('cpu')
    vae = model.get_vae()
    if isinstance(vae, nn.Module) and next(vae.parameters()).device.type != 'meta':
        vae.to('cpu')


def _prepare_conditions(model, config):
    _ensure_sampling_adapters(model)
    try:
        model.prepare_sample_test(
            config['positive_prompt'],
            negative_prompt=config['negative_prompt'],
            cfg=config['cfg'],
        )
    finally:
        _move_aux_models_to_cpu(model)
        gc.collect()
        torch.cuda.empty_cache()


@torch.no_grad()
@torch.autocast('cuda', dtype=AUTOCAST_DTYPE)
def _denoise(model, config):
    if config['sampler'] not in {'euler', 'flow_euler'}:
        raise ValueError(f"unsupported preview sampler: {config['sampler']}")
    if config['scheduler'] != 'normal':
        raise ValueError(f"unsupported preview scheduler: {config['scheduler']}")

    scheduler = FlowMatchEulerDiscreteScheduler(shift=3)
    sigmas = torch.linspace(1.0, 1 / config['steps'], config['steps'])
    scheduler.set_timesteps(sigmas=sigmas, device='cuda')
    generator = torch.Generator(device='cuda').manual_seed(config['seed'])
    latents = torch.randn(
        (1, model.channels, 1, config['height'] // model.spatial_compression, config['width'] // model.spatial_compression),
        generator=generator,
        device='cuda',
    )
    for step in tqdm(scheduler.timesteps, desc='Anima preview'):
        timestep = (step / 1000).float().view(1)
        velocity = model.pipeline_model((latents, timestep, *model.conds)).float()
        if config['cfg'] > 1:
            uncond_velocity = model.pipeline_model((latents, timestep, *model.unconds)).float()
            velocity = uncond_velocity + config['cfg'] * (velocity - uncond_velocity)
            del uncond_velocity
        latents = scheduler.step(velocity, step, latents, return_dict=False)[0]
        del velocity
    del scheduler, sigmas, generator
    return latents


def _offload_pipeline_for_vae(saver, config):
    if not config['offload_pipeline_for_vae']:
        return False
    if saver.config.get('blocks_to_swap', 0):
        print('[Anima preview] block swap is active; skipping whole-pipeline VAE offload.')
        return False
    saver.pipeline_model.to('cpu')
    torch.cuda.synchronize()
    gc.collect()
    torch.cuda.empty_cache()
    _log_vram('after pipeline offload, before VAE decode')
    return True


def _decode_to_cpu(saver, latents, config):
    pipeline_offloaded = _offload_pipeline_for_vae(saver, config)
    vae = saver.model.get_vae()
    try:
        vae.to('cuda')
        image = saver.model.vae_decode(latents)
        image = image.squeeze(0)
        if image.ndim == 4:
            if image.shape[0] != 1:
                raise ValueError(f'Anima image preview expected one frame, got shape {tuple(image.shape)}')
            image = image.squeeze(0)
        if image.ndim != 3:
            raise ValueError(f'Anima image preview expected HWC output, got shape {tuple(image.shape)}')
        return image.movedim(-1, 0).cpu()
    finally:
        vae.to('cpu')
        del latents
        gc.collect()
        torch.cuda.empty_cache()
        _log_vram('after VAE cleanup')
        if pipeline_offloaded:
            saver.pipeline_model.to('cuda')
            torch.cuda.synchronize()


def _unique_output_path(saver, epoch, step, config):
    output_dir = Path(saver.save_root) / 'sample'
    output_dir.mkdir(parents=True, exist_ok=True)
    sampler = re.sub(r'[^a-zA-Z0-9_-]+', '_', config['sampler'])
    scheduler = re.sub(r'[^a-zA-Z0-9_-]+', '_', config['scheduler'])
    stem = f'preview_epoch_{epoch:04d}_step_{step:06d}_{sampler}_{scheduler}_seed_{config["seed"]}'
    path = output_dir / f'{stem}.png'
    suffix = 2
    while path.exists():
        path = output_dir / f'{stem}_{suffix}.png'
        suffix += 1
    return path


def _save_preview(saver, epoch, step, config):
    torch.cuda.reset_peak_memory_stats()
    _log_vram('before preview')
    pipeline_model = saver.pipeline_model
    was_training = pipeline_model.training
    image = None
    try:
        pipeline_model.eval()
        saver.model.prepare_block_swap_inference()
        _prepare_conditions(saver.model, config)
        _log_vram('after prompt encoding')
        latents = _denoise(saver.model, config)
        del saver.model.conds
        if hasattr(saver.model, 'unconds'):
            del saver.model.unconds
        gc.collect()
        torch.cuda.empty_cache()
        _log_vram('after denoising, before pipeline offload')
        image = _decode_to_cpu(saver, latents, config)
        _log_vram('after sampling and VAE decode')
        output_path = _unique_output_path(saver, epoch, step, config)
        torchvision.utils.save_image(image, output_path)
        print(f'Saved preview image to {output_path}')
    finally:
        if image is not None:
            del image
        _move_aux_models_to_cpu(saver.model)
        if next(pipeline_model.parameters()).device.type != 'cuda':
            pipeline_model.to('cuda')
        saver.model.prepare_block_swap_training()
        pipeline_model.train(was_training)
        for attr in ('conds', 'unconds'):
            if hasattr(saver.model, attr):
                delattr(saver.model, attr)
        gc.collect()
        torch.cuda.empty_cache()
        _log_vram('after preview state restore')


_original_dataset_manager_init = DatasetManager.__init__


def _dataset_manager_init(self, model, *args, **kwargs):
    try:
        preview_enabled = _is_anima(model) and _preview_config(model)['enabled']
    except Exception as exc:
        print(f'[Anima preview] invalid config; keeping preview submodels available: {exc}')
        preview_enabled = _is_anima(model)
    if preview_enabled and is_main_process():
        kwargs['keep_models_loaded'] = True
    _original_dataset_manager_init(self, model, *args, **kwargs)


DatasetManager.__init__ = _dataset_manager_init


_original_process_epoch = Saver.process_epoch


def _process_epoch(self, epoch, step, examples):
    result = _original_process_epoch(self, epoch, step, examples)
    new_epoch = result[0]
    if not _is_anima(self.model) or new_epoch == epoch:
        return result
    try:
        config = _preview_config(self.model)
    except Exception as exc:
        print(f'[Anima preview] skipped epoch {epoch}: invalid config: {exc}')
        return result
    if not config['enabled'] or epoch % config['every_n_epochs'] != 0:
        return result
    if not config['positive_prompt']:
        print(f'[Anima preview] skipped epoch {epoch}: positive prompt is empty.')
        return result
    if self.config.get('pipeline_stages', 1) != 1:
        print(f'[Anima preview] skipped epoch {epoch}: pipeline_stages must be 1.')
        return result

    dist.barrier()
    if is_main_process():
        try:
            _save_preview(self, epoch, step, config)
        except Exception as exc:
            print(f'[Anima preview] epoch {epoch} failed without stopping training: {exc}')
            traceback.print_exc()
    dist.barrier()
    return result


Saver.process_epoch = _process_epoch
