using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Pipeline
{
    public interface IPipelineRunnableViewModel
    {
        Task RunFromPipelineAsync(PipelineContext context, CancellationToken token);
    }
}
