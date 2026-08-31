using System;
using Azure.Identity;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace LogicAppStorageInspector.Services
{
    // Builds storage clients; prefers managed identity (AzureWebJobsStorage__accountName),
    // falls back to the AzureWebJobsStorage connection string.
    public sealed class StorageContext
    {
        public TableServiceClient Tables { get; }
        public QueueServiceClient Queues { get; }
        public BlobServiceClient Blobs { get; }

        public StorageContext()
        {
            var accountName = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName");
            var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            if (!string.IsNullOrWhiteSpace(accountName))
            {
                var cred = new DefaultAzureCredential();
                Tables = new TableServiceClient(new Uri($"https://{accountName}.table.core.windows.net"), cred);
                Queues = new QueueServiceClient(new Uri($"https://{accountName}.queue.core.windows.net"), cred);
                Blobs = new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), cred);
            }
            else if (!string.IsNullOrWhiteSpace(conn))
            {
                Tables = new TableServiceClient(conn);
                Queues = new QueueServiceClient(conn);
                Blobs = new BlobServiceClient(conn);
            }
            else
            {
                throw new InvalidOperationException("No storage configuration. Set AzureWebJobsStorage or AzureWebJobsStorage__accountName.");
            }
        }
    }
}