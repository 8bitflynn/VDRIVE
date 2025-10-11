using System.Diagnostics;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("ImagePath={ImagePath}, Id={Id}")]
    public struct FloppyPointer
    {
        public string ImagePath;
        public ushort Id;
    }
}
