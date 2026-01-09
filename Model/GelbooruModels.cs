namespace CyanTools.Model
{
    public class Gelbooru
    {
        public class Post
        {
            public int id { get; set; }
            public string file_url { get; set; }
            public string sample_url { get; set; }
            public string source { get; set; }
            public string tags { get; set; }
            public string md5 { get; set; }
            public string file_ext { get; set; }
        }
    }
}
