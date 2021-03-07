using System;
using System.Threading.Tasks;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Models;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface ITermService
    {
        Task<Term> GetTermAsync(Func<ITermService, Task<Term>> onFailure = null);
        Task SetTermAsync(Term term);
    }

    internal class TermService : ITermService
    {
        public TermService(CosmosClient cosmosClient, IConfiguration config)
        {
            string databaseName = config.GetValue<string>("Database:Database");
            string containerName = config.GetValue<string>("Database:Container");
            container = cosmosClient.GetContainer(databaseName, containerName);
        }

        public async Task<Term> GetTermAsync(Func<ITermService, Task<Term>> onFailure)
        {
            if (!cached)
            {
                Term term;
                try
                {
                    ItemResponse<Term> response = await container.ReadItemAsync<Term>("Term", new PartitionKey("Term"));
                    term = response.Value;
                }
                catch (Exception)
                {
                    if (onFailure == null)
                    {
                        throw;
                    }
                    term = await onFailure(this);
                }

                lock (cacheLock)
                {
                    if (!cached)
                    {
                        cachedTerm = term;
                        cached = true;
                    }
                }
            }
            return cachedTerm;
        }

        public async Task SetTermAsync(Term term)
        {
            lock (cacheLock)
            {
                cachedTerm = term;
                cached = true;
            }
            await container.UpsertItemAsync(term, new PartitionKey("Term"));
        }

        private readonly CosmosContainer container;
        private static readonly object cacheLock = new object();
        private static bool cached;
        private static Term cachedTerm;
    }
}
