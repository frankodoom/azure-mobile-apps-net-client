﻿// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MobileServices.Sync;
using Microsoft.WindowsAzure.MobileServices.SQLiteStore.Test;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.MobileServices.TestFramework;
using Microsoft.WindowsAzure.MobileServices.Test;

namespace Microsoft.WindowsAzure.MobileServices.SQLiteStore.Test.UnitTests
{
    public class SQLiteStoreIntegrationTests: TestBase
    {
        private const string TestDbName = "test.db";
        private const string TestTable = "stringId_test_table";

        [AsyncTestMethod]
        public async Task ReadAsync_RoundTripsDate()
        {
            string tableName = "itemWithDate";

            ResetDatabase(tableName);

            var store = new MobileServiceSQLiteStore(TestDbName);
            store.DefineTable(tableName, new JObject()
            {
                { "id", String.Empty},
                { "date", DateTime.Now }
            });

            var hijack = new TestHttpHandler();
            IMobileServiceClient service = await CreateClient(hijack, store);
            IMobileServiceSyncTable table = service.GetSyncTable(tableName);

            DateTime theDate = new DateTime(2014, 3, 10, 0, 0, 0, DateTimeKind.Utc);
            JObject inserted = await table.InsertAsync(new JObject() { { "date",  theDate} });
            
            Assert.AreEqual(inserted["date"].Value<DateTime>(), theDate);

            JObject rehydrated = await table.LookupAsync(inserted["id"].Value<string>());

            Assert.AreEqual(rehydrated["date"].Value<DateTime>(), theDate);
        }

        [AsyncTestMethod]
        public async Task DefineTable_IgnoresColumn_IfCaseIsDifferentButNameIsSame()
        {
            string tableName = "itemWithDate";

            ResetDatabase(tableName);

            var store = new MobileServiceSQLiteStore(TestDbName);
            store.DefineTable(tableName, new JObject()
            {
                { "id", String.Empty},
                { "date", DateTime.Now }
            });

            var hijack = new TestHttpHandler();
            await CreateClient(hijack, store);

            store = new MobileServiceSQLiteStore(TestDbName);
            store.DefineTable(tableName, new JObject()
            {
                { "id", String.Empty},
                { "DaTE", DateTime.Now } // the casing of date is different here
            });
            hijack = new TestHttpHandler();
            await CreateClient(hijack, store);
        }

        [AsyncTestMethod]
        public async Task Insert_ThenPush_ThenPull_ThenRead_ThenUpdate_ThenRefresh_ThenDelete_ThenLookup_ThenPurge_ThenRead()
        {
            ResetDatabase(TestTable);

            var hijack = new TestHttpHandler();
            hijack.AddResponseContent("{\"id\":\"b\",\"String\":\"Hey\"}"); // insert response
            hijack.AddResponseContent("[{\"id\":\"b\",\"String\":\"Hey\"},{\"id\":\"a\",\"String\":\"World\"}]"); // pull response

            var store = new MobileServiceSQLiteStore(TestDbName);
            store.DefineTable<ToDoWithStringId>();

            IMobileServiceClient service = await CreateClient(hijack, store);
            IMobileServiceSyncTable<ToDoWithStringId> table = service.GetSyncTable<ToDoWithStringId>();

            // first insert an item
            await table.InsertAsync(new ToDoWithStringId() { Id = "b", String = "Hey" }); 
            
            // then push it to server
            await service.SyncContext.PushAsync(); 

            // then pull changes from server
            await table.PullAsync();

            // order the records by id so we can assert them predictably 
            IList<ToDoWithStringId> items = await table.OrderBy(i => i.Id).ToListAsync();

            // we should have 2 records 
            Assert.AreEqual(items.Count, 2);

            // according to ordering a id comes first
            Assert.AreEqual(items[0].Id, "a");
            Assert.AreEqual(items[0].String, "World");

            // then comes b record
            Assert.AreEqual(items[1].Id, "b");
            Assert.AreEqual(items[1].String, "Hey");

            // we made 2 requests, one for push and one for pull
            Assert.AreEqual(hijack.Requests.Count, 2);

            // recreating the client from state in the store
            service = await CreateClient(hijack, store);
            table = service.GetSyncTable<ToDoWithStringId>();

            // update the second record
            items[1].String = "Hello";
            await table.UpdateAsync(items[1]);

            // create an empty record with same id as modified record
            var second = new ToDoWithStringId() { Id = items[1].Id };
            // refresh the empty record
            await table.RefreshAsync(second);

            // make sure it is same as modified record now
            Assert.AreEqual(second.String, items[1].String);

            // now delete the record
            await table.DeleteAsync(second);

            // now try to get the deleted record
            ToDoWithStringId deleted = await table.LookupAsync(second.Id);

            // this should be null
            Assert.IsNull(deleted);

            // try to get the non-deleted record
            ToDoWithStringId first = await table.LookupAsync(items[0].Id);

            // this should still be there;
            Assert.IsNotNull(first);

            // make sure it is same as 
            Assert.AreEqual(first.String, items[0].String);

            // recreating the client from state in the store
            service = await CreateClient(hijack, store);
            table = service.GetSyncTable<ToDoWithStringId>();

            // now purge the remaining records
            await table.PurgeAsync();

            // now read one last time
            IEnumerable<ToDoWithStringId> remaining = await table.ReadAsync();

            // There shouldn't be anything remaining
            Assert.AreEqual(remaining.Count(), 0);
        }

        private static async Task<IMobileServiceClient> CreateClient(TestHttpHandler hijack, MobileServiceSQLiteStore store)
        {
            IMobileServiceClient service = new MobileServiceClient("http://www.test.com", "secret...", hijack);
            await service.SyncContext.InitializeAsync(store, new MobileServiceSyncHandler());
            return service;
        }

        private static void ResetDatabase(string testTableName)
        {
            TestUtilities.DropTestTable(TestDbName, testTableName);
            TestUtilities.DropTestTable(TestDbName, LocalSystemTables.OperationQueue);
            TestUtilities.DropTestTable(TestDbName, LocalSystemTables.SyncErrors);
        }
    }
}
