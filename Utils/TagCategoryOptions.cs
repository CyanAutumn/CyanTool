namespace CyanTools.Utils
{
    public class TagCategoryOptions
    {
        public bool IncludeGeneral { get; set; } = true;
        public bool IncludeArtist { get; set; } = false;
        public bool IncludeCopyright { get; set; } = false;
        public bool IncludeCharacter { get; set; } = true;
        public bool IncludeMeta { get; set; } = false;
        public bool IncludeQuality { get; set; } = false;
        public bool IncludeRating { get; set; } = false;
        public bool IncludeModel { get; set; } = false;
    }
}
