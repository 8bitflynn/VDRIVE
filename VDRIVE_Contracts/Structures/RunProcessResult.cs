namespace VDRIVE_Contracts.Structures
{
    public class RunProcessResult
    {
        public string Output {  get; set; }
        public bool HasError { get { return !string.IsNullOrWhiteSpace(this.Error);  } }
        public string Error { get; set; }
    }
}
