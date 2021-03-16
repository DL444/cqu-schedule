using System;
using System.Threading.Tasks;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Models;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface ITermService
    {
        Task<Term> GetTermAsync(Func<ITermService, Task<Term>> onFailure = null);
        Task SetTermAsync(Term term);
    }

    internal class TermService : ITermService
    {
        public TermService(CosmosContainer container) => this.container = container;

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
