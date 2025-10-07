using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VDRIVE.Structures;

namespace VDRIVE.Contracts
{
    public interface ILoad
    {
        LoadResponse Load(LoadRequest loadRequest, string imagePath, out byte[] payload);
    }
}
