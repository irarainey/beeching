namespace Beeching.Models
{
    internal class AxeStatus
    {
        public List<(Uri, string)> AxeList { get; set; }

        public bool Status { get; set; }

        public AxeStatus()
        {
            AxeList = new();
            Status = true;
        }
    }
}
