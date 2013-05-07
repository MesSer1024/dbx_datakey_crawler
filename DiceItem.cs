using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Search_DBX_files
{
    public class DiceItem
    {
        public string Guid { get; set; }
        public string Identifier { get; set; }
        private string _visibleName;
        public string VisibleName
        {
            get { return _visibleName ?? Identifier; }
            set { _visibleName = value; }
        }
        public int ReferenceCount { get; set; }
    }
}
