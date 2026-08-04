using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace CyanTools.Navigation
{
    public static class ToolPageRegistry
    {
        private static readonly HashSet<Type> HiddenControls = new HashSet<Type>
        {
            typeof(global::CyanTools.LoraTrainingView),
            typeof(global::CyanTools.ModelTrainingView),
            typeof(global::CyanTools.HandIntegrityFilterView)
        };
        private static IReadOnlyList<ToolPageMetadata>? _cached;

        public static IReadOnlyList<ToolPageMetadata> GetAllPages()
        {
            if (_cached != null)
            {
                return _cached;
            }

            var assembly = typeof(ToolPageRegistry).Assembly;
            _cached = assembly
                .GetTypes()
                .Where(t => typeof(UserControl).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<ToolPageAttribute>() })
                .Where(x => x.Attribute != null && !HiddenControls.Contains(x.Type))
                .Select(x => new ToolPageMetadata(
                    x.Attribute!.DisplayName,
                    x.Type,
                    x.Attribute.Order,
                    x.Attribute.RequiresUnlock,
                    GetCategoryForControl(x.Type)))
                .OrderBy(x => x.Order)
                .ToList();

            return _cached;
        }

        public static void ResetCache() => _cached = null;

        private static ToolPageCategory GetCategoryForControl(Type controlType)
        {
            if (controlType == typeof(global::CyanTools.PipelineSchedulerView) ||
                controlType == typeof(global::CyanTools.TrainingSetBalancerView) ||
                controlType == typeof(global::CyanTools.XyzToolView) ||
                controlType == typeof(global::CyanTools.ToolLLM) ||
                controlType == typeof(global::CyanTools.ModelTrainingView) ||
                controlType == typeof(global::CyanTools.TrainingSetManagerView) ||
                controlType == typeof(global::CyanTools.TrainingBridgeView) ||
                controlType == typeof(global::CyanTools.LayeredLoraMergeView) ||
                controlType == typeof(global::CyanTools.ModelMetadataView) ||
                controlType == typeof(global::CyanTools.LoraTrainingView))
            {
                return ToolPageCategory.Training;
            }

            if (controlType == typeof(global::CyanTools.DanbooruCrawlerView) ||
                controlType == typeof(global::CyanTools.GeminiImageGenerationView))
            {
                return ToolPageCategory.DatasetAcquisition;
            }

            if (controlType == typeof(global::CyanTools.ImageBucketingView) ||
                controlType == typeof(global::CyanTools.ThreeStageView) ||
                controlType == typeof(global::CyanTools.ResolutionFilterView) ||
                controlType == typeof(global::CyanTools.ImageClusteringView) ||
                controlType == typeof(global::CyanTools.SameSourceImageGroupingView) ||
                controlType == typeof(global::CyanTools.KeepFilesView) ||
                controlType == typeof(global::CyanTools.HandIntegrityFilterView))
            {
                return ToolPageCategory.DatasetClassification;
            }

            if (controlType == typeof(global::CyanTools.OptionsView) ||
                controlType == typeof(global::CyanTools.InferenceProjectsView))
            {
                return ToolPageCategory.EnvironmentSettings;
            }

            return ToolPageCategory.DatasetProcessing;
        }
    }
}
