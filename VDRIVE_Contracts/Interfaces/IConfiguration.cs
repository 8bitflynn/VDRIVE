namespace VDRIVE_Contracts.Interfaces
{
    public interface IConfiguration
    {
        string C1541Path { get; set; }
        List<string> SearchPaths { get; set; }
    }
}