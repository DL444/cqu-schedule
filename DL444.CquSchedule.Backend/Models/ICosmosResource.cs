namespace DL444.CquSchedule.Backend.Models
{
    internal interface ICosmosResource
    {
        string Id { get; }
        string PartitionKey { get; }
    }
}
