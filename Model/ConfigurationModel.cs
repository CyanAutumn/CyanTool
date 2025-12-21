
using System.Collections.Generic;

namespace CyanTools.Model
{
    public class ConfigurationModel
    {
        public string Name { get; set; }
        public string Prompt { get; set; }
        public string WorkingDirectory { get; set; }
        public List<string> Paths { get; set; }
        public bool IsReasoning { get; set; }
    }
}
