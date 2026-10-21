namespace VDRIVE_Contracts.Structures
{
    public class HcsvPsid
    {
        public string BaseURL { get; set; }
        public string ExecutablePath { get; set; }
        public string Arguments { get; set; }
        public string LocalPath { get; set; }       // optional: a local HVSC copy (its C64Music folder), searched before the web (Util/HvscIndex)
        public string SidRelocPath { get; set; }    // optional: sidreloc, to move a tune to $1000 for a program's own player (SidTune)
    }
}
