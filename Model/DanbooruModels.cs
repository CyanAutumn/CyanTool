using System;
using System.Collections.Generic;

namespace CyanTools.Model
{
    public class Danbooru
    {
        public class Post
        {
            public int id { get; set; }
            public DateTime created_at { get; set; }
            public int uploader_id { get; set; }
            public int score { get; set; }
            public string source { get; set; }
            public string md5 { get; set; }
            public object last_comment_bumped_at { get; set; }
            public string rating { get; set; }
            public int image_width { get; set; }
            public int image_height { get; set; }
            public string tag_string { get; set; }
            public int fav_count { get; set; }
            public string file_ext { get; set; }
            public object last_noted_at { get; set; }
            public object parent_id { get; set; }
            public bool has_children { get; set; }
            public object approver_id { get; set; }
            public int tag_count_general { get; set; }
            public int tag_count_artist { get; set; }
            public int tag_count_character { get; set; }
            public int tag_count_copyright { get; set; }
            public int file_size { get; set; }
            public int up_score { get; set; }
            public int down_score { get; set; }
            public bool is_pending { get; set; }
            public bool is_flagged { get; set; }
            public bool is_deleted { get; set; }
            public int tag_count { get; set; }
            public DateTime updated_at { get; set; }
            public bool is_banned { get; set; }
            public object pixiv_id { get; set; }
            public object last_commented_at { get; set; }
            public bool has_active_children { get; set; }
            public int bit_flags { get; set; }
            public int tag_count_meta { get; set; }
            public bool has_large { get; set; }
            public bool has_visible_children { get; set; }
            public MediaAsset media_asset { get; set; }
            public string tag_string_general { get; set; }
            public string tag_string_character { get; set; }
            public string tag_string_copyright { get; set; }
            public string tag_string_artist { get; set; }
            public string tag_string_meta { get; set; }
            public string file_url { get; set; }
            public string large_file_url { get; set; }
            public string preview_file_url { get; set; }
        }

        public class MediaAsset
        {
            public int id { get; set; }
            public DateTime created_at { get; set; }
            public DateTime updated_at { get; set; }
            public string md5 { get; set; }
            public string file_ext { get; set; }
            public int file_size { get; set; }
            public int image_width { get; set; }
            public int image_height { get; set; }
            public object duration { get; set; }
            public string status { get; set; }
            public string file_key { get; set; }
            public bool is_public { get; set; }
            public string pixel_hash { get; set; }
            public List<Variant> variants { get; set; }
        }

        public class Variant
        {
            public string type { get; set; }
            public string url { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public string file_ext { get; set; }
        }
    }
}
