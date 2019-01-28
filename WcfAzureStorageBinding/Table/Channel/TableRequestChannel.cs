﻿namespace AzureStorageBinding.Table.Channel
{
    using System;
    using System.Diagnostics;
    using System.ServiceModel;
    using System.ServiceModel.Channels;
    using System.Threading.Tasks;

    using AzureStorageBinding.Table.DTO;
    using AzureStorageBinding.Table.Utils;
    using AzureStorageBinding.Utils.Apm;

    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Table;

    internal class TableRequestChannel : TableChannel, IRequestChannel
    {
        public TableRequestChannel(
            ChannelManagerBase channelManager,
            CloudTableClient tableClient,
            string targetPartition,
            BufferManager bufferManager,
            EndpointAddress address,
            MessageEncoder encoder,
            Uri via) : base(channelManager, tableClient, via.AbsolutePath, targetPartition, bufferManager, address, encoder)
        {
            this.Via = via;
        }

        protected override void OnAbort()
        {
        }

        protected override void OnClose(TimeSpan timeout)
        {
        }

        protected override void OnEndClose(IAsyncResult result)
        {
        }

        protected override IAsyncResult OnBeginClose(TimeSpan timeout, AsyncCallback callback, object state) => CompletedAsyncResult.Create( callback, state);

        protected override void OnOpen(TimeSpan timeout)
        {
        }

        protected override IAsyncResult OnBeginOpen(TimeSpan timeout, AsyncCallback callback, object state) => CompletedAsyncResult.Create( callback, state);

        protected override void OnEndOpen(IAsyncResult result)
        {
        }

        public Message Request(Message message) => this.Request(message, this.DefaultReceiveTimeout);

        public Message Request(Message message, TimeSpan timeout) => this.RequestAsync(message, timeout).GetAwaiter().GetResult();

        public IAsyncResult BeginRequest(Message message, AsyncCallback callback, object state) => this.BeginRequest(message, this.DefaultReceiveTimeout, callback, state);

        public IAsyncResult BeginRequest(Message message, TimeSpan timeout, AsyncCallback callback, object state) => this.RequestAsync(message, timeout).AsApm(callback, state);

        public Message EndRequest(IAsyncResult result) => ((Task<Message>)result).Result;

        public Uri Via { get; }

        public async Task<Message> RequestAsync(Message requestMessage, TimeSpan timeout)
        {
            this.ThrowIfDisposedOrNotOpen();

            try
            {
                string reqId = Guid.NewGuid().ToString();

                // Write the request message
                await this.WriteRequestMessageAsync(requestMessage, reqId).ConfigureAwait(false);

                // Wait for the response
                return await this.TryGetReplyMessageAsync(reqId, timeout);
            }
            catch (StorageException exception)
            {
                throw new CommunicationException(exception.Message, exception);
            }
        }

        private async Task<Message> TryGetReplyMessageAsync(string reqId, TimeSpan timeout)
        {
            Debug.Assert(!string.IsNullOrEmpty(this.TargetPartition));
            Debug.Assert(this.TargetPartition != TableConstants.TargetAllPartitionToken);
            Debug.Assert(!string.IsNullOrEmpty(reqId));
            var queryPartKey = TableQuery.GenerateFilterCondition(TableConstants.PartitionKeyPropertyName, QueryComparisons.Equal, this.TargetPartition);
            var queryRowKey = TableQuery.GenerateFilterCondition(TableConstants.RowKeyPropertyName, QueryComparisons.Equal, reqId);
            var filterClause = TableQuery.CombineFilters(queryPartKey, TableOperators.And, queryRowKey);
            var tableQuery = new TableQuery<WcfTableEntity>().Where(filterClause).Take(1);
            try
            {
                var (succeed, res) = await this.TryPopMessageFromTableAsync(this.ResponseTableName, tableQuery, timeout);
                if (succeed)
                {
                    throw new TimeoutException(timeout.ToString());
                }
                else
                {
                    return res;
                }
            }
            catch (StorageException exception)
            {
                throw new CommunicationException(exception.Message, exception);
            }
        }

        private Task WriteRequestMessageAsync(Message message, string requestId)
        {
            return this.WriteMessageAsync(this.RequestTableName, message, this.TargetPartition, requestId);
        }
    }
}