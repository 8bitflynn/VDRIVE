using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SearchFloppyResponse
    {
        public SearchFloppyResponse() { }

        public IList<FloppyInfo> SearchResults = new List<FloppyInfo>();        
    }
}
