namespace VDRIVE_Contracts.Interfaces
{
    public interface IConfigurationBuilder
    {
        IConfiguration BuildConfiguration();
        bool IsValidConfiguration(IConfiguration configuration);
        void DumpConfiguration(IConfiguration configuration);
    }
}
