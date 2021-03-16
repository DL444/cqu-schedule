using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Models;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IDataService
    {
        Task<List<string>> GetUserIdsAsync();
        Task<User> GetUserAsync(string username);
        Task SetUserAsync(User user);
        Task<Schedule> GetScheduleAsync(string username);
        Task SetScheduleAsync(Schedule schedule);
        Task<bool> DeleteUserAsync(string username);
    }

    internal class DataService : IDataService
    {
        public DataService(CosmosContainer container) => this.container = container;

        public async Task<List<string>> GetUserIdsAsync()
        {
            var users = new List<string>();
            QueryDefinition usersQuery = new QueryDefinition("SELECT c.Username FROM c WHERE c.PartitionKey = \"User\"");
            await foreach (UsernameHeader header in container.GetItemQueryIterator<UsernameHeader>(usersQuery, requestOptions: new QueryRequestOptions()
            {
                PartitionKey = new PartitionKey("User")
            }))
            {
                users.Add(header.Username);
            }
            return users;
        }

        public Task<User> GetUserAsync(string username) => GetResourceAsync<User>($"User-{username}", "User");
        public Task SetUserAsync(User user) => SetResourceAsync(user);

        public Task<Schedule> GetScheduleAsync(string username) => GetResourceAsync<Schedule>($"Schedule-{username}", username);
        public Task SetScheduleAsync(Schedule schedule) => SetResourceAsync(schedule);

        public async Task<bool> DeleteUserAsync(string username)
        {
            Task userDeleteTask = container.DeleteItemAsync<object>($"User-{username}", new PartitionKey("User"));
            Task scheduleDeleteTask = container.DeleteItemAsync<object>($"Schedule-{username}", new PartitionKey(username));
            bool hasError = false;
            try
            {
                await Task.WhenAll(userDeleteTask, scheduleDeleteTask);
            }
            catch (AggregateException aggregateException)
            {
                aggregateException.Handle(ex =>
                {
                    if (!(ex is CosmosException cosmosEx && cosmosEx.Status == 404))
                    {
                        hasError = true;
                    }
                    return true;
                });
            }
            catch (CosmosException ex)
            {
                if (ex.Status != 404)
                {
                    hasError = true;
                }
            }
            catch (Exception)
            {
                hasError = true;
            }

            return !hasError;
        }

        private async Task<T> GetResourceAsync<T>(string id, string partition) where T : ICosmosResource
        {
            ItemResponse<T> response = await container.ReadItemAsync<T>(id, new PartitionKey(partition));
            return response.Value;
        }
        private Task SetResourceAsync<T>(T resource) where T : ICosmosResource => container.UpsertItemAsync(resource, new PartitionKey(resource.PartitionKey));

        private struct UsernameHeader
        {
            public string Username { get; set; }
        }

        private readonly CosmosContainer container;
    }
}
