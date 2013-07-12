using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Search_DBX_files
{
    class SavedData
    {
        public List<DiceItem> Items { get; set; }
        public Dictionary<String, List<FileInfo>> Files { get; set; }
        public Dictionary<String, List<String>> Lines { get; set; }
    }
}
