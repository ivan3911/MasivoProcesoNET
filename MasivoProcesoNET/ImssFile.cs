using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasivoProcesoNET
{
    class ImssFile
    {
        private string name = string.Empty;
        private string fullname = string.Empty;
        long size = 0;

        public ImssFile(string name, string fullname, long size)
        {
            this.name = name;
            this.fullname = fullname;
            this.size = size;
        }

        public string Name { get => name;}
        public string Fullname { get => fullname;}
        public long Size { get => size; }
    }
}
