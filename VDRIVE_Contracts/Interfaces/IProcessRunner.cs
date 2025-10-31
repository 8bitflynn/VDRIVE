using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IProcessRunner
    {
        RunProcessResult RunProcess(RunProcessParameters runProcessParameters);
    }
}
