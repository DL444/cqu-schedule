using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Models;
using Microsoft.Azure.Cosmos;
using User = DL444.CquSchedule.Backend.Models.User;

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
        Task<ServiceStatusContainer> GetServiceStatusAsync();
    }

    internal class DataService : IDataService
    {
        public DataService(Container container) => this.container = container;

        public async Task<List<string>> GetUserIdsAsync()
        {
            var users = new List<string>();
            var usersQuery = new QueryDefinition("SELECT c.Username FROM c WHERE c.PartitionKey = \"User\"");
            var requestOptions = new QueryRequestOptions()
            {
                PartitionKey = new PartitionKey("User")
            };
            using (FeedIterator<UsernameHeader> feedIterator = container.GetItemQueryIterator<UsernameHeader>(usersQuery, requestOptions: requestOptions))
            {
                while (feedIterator.HasMoreResults)
                {
                    IEnumerable<UsernameHeader> batch = await feedIterator.ReadNextAsync();
                    users.AddRange(batch.Select(x => x.Username));
                }
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
                    if (!(ex is CosmosException cosmosEx && cosmosEx.StatusCode == HttpStatusCode.NotFound))
                    {
                        hasError = true;
                    }
                    return true;
                });
            }
            catch (CosmosException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
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

        public Task<ServiceStatusContainer> GetServiceStatusAsync() => GetResourceAsync<ServiceStatusContainer>("Status", "Status");

        private async Task<T> GetResourceAsync<T>(string id, string partition) where T : ICosmosResource
        {
            ItemResponse<T> response = await container.ReadItemAsync<T>(id, new PartitionKey(partition));
            return response.Resource;
        }
        private Task SetResourceAsync<T>(T resource) where T : ICosmosResource => container.UpsertItemAsync(resource, new PartitionKey(resource.PartitionKey));

        private struct UsernameHeader
        {
            public string Username { get; set; }
        }

        private readonly Container container;
    }
}
