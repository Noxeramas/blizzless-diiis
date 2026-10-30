using System.Reflection;
using FluentNHibernate;
using NHibernate.Cfg;
using NHibernate.Tool.hbm2ddl;
using Npgsql;
using DiIiS_NA.Core.Helpers.Hash;
using DiIiS_NA.Core.Storage.AccountDataBase;
using DiIiS_NA.Core.Storage.AccountDataBase.Entities;
using static DiIiS_NA.Core.MPQ.FileFormats.GameBalance;

static class DatabaseChecks
{
    // Opt-in only. The supplied local config supplies credentials; all test data lives in a new disposable database.
    public static void Run(string configPath, Type cube, TransmuteRecipesTable recipe)
    {
        var config = new Configuration().Configure(Path.GetFullPath(configPath));
        var connection = new NpgsqlConnectionStringBuilder(config.GetProperty("connection.connection_string"));
        Require(connection.Host is "localhost" or "127.0.0.1" or "::1", "Database checks require a local PostgreSQL server");
        connection.Pooling = false;
        using var admin = new NpgsqlConnection(connection.ConnectionString);
        admin.Open();
        var database = "cube_regression_" + Guid.NewGuid().ToString("N");
        new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQuery();
        try
        {
            connection.Database = database;
            config.SetProperty("connection.connection_string", connection.ConnectionString);
            config.AddMappingsFromAssembly(typeof(DBActiveSkills).Assembly);
            new SchemaExport(config).Create(false, true);
            using var factory = config.BuildSessionFactory();
            var factoryField = typeof(SessionProvider).GetField("_sessionFactory", BindingFlags.NonPublic | BindingFlags.Static)!;
            factoryField.SetValue(null, factory);
            try
            {
                using var sql = new NpgsqlConnection(connection.ConnectionString);
                sql.Open();
                void Execute(string text) => new NpgsqlCommand(text, sql).ExecuteNonQuery();
                T Read<T>(ulong id) { using var session = factory.OpenStatelessSession(); return session.Get<T>(id); }
                void Save(object value) { using var session = factory.OpenStatelessSession(); session.Insert(value); }
                void Update(object value) { using var session = factory.OpenStatelessSession(); session.Update(value); }
                object Call(string method, params object[] values) => cube.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, values)!;
                object Power(string name) => Call("GetPower", StringHashHelper.HashItemName(name));

                var skills = new DBActiveSkills();
                Save(skills);
                Execute("ALTER TABLE skills DROP COLUMN legendarypower0, DROP COLUMN legendarypower1, DROP COLUMN legendarypower2");
                var migration = new SchemaUpdate(config);
                migration.Execute(false, true);
                Require(migration.Exceptions.Count == 0, "Cube column migration failed");
                skills = Read<DBActiveSkills>(skills.Id);
                Require(skills.LegendaryPower0 == -1 && skills.LegendaryPower1 == -1 && skills.LegendaryPower2 == -1, "Existing hero was not initialized with empty cube slots");
                skills.LegendaryPower0 = -844626822;
                Update(skills);
                Require(Read<DBActiveSkills>(skills.Id).LegendaryPower0 == -844626822, "Assignment did not survive a new database session");

                DBGameAccount Account() => new() { CraftItem4 = 100, HoradricA1 = 100, HoradricA2 = 100, HoradricA3 = 100, HoradricA4 = 100, HoradricA5 = 100,
                    HardCraftItem4 = 100, HardHoradricA1 = 100, HardHoradricA2 = 100, HardHoradricA3 = 100, HardHoradricA4 = 100, HardHoradricA5 = 100 };
                var account = Account();
                account.HoradricA5 = 0;
                Save(account);
                DBInventory Item(string name = "P610_Unique_Sword_1H_107")
                {
                    var item = new DBInventory { DBGameAccount = account, GbId = StringHashHelper.HashItemName(name), Attributes = "", Affixes = "" };
                    Save(item);
                    return item;
                }
                var sword = Power("P610_Unique_Sword_1H_107");
                int Extract(DBInventory item, bool hardcore = false, bool seasonal = false, int season = 0, object power = null) =>
                    (int)Call("StoreExtractedPower", account.Id, item.Id, hardcore, seasonal, season, recipe, power ?? sword);
                var first = Item();
                Require(Extract(first) == 162, "Insufficient materials did not fail");
                Require(Read<DBInventory>(first.Id) != null && Read<DBGameAccount>(account.Id).CraftItem4 == 100, "Failure consumed an item or materials");
                account.HoradricA5 = 100; Update(account);
                Require(Extract(first) == 0, "Valid extraction failed");
                Require(Read<DBInventory>(first.Id) == null && Read<DBGameAccount>(account.Id).CraftItem4 == 95, "Successful extraction did not consume the exact inputs");
                Require(Extract(first) == 170, "Replayed deleted item was accepted");
                var duplicate = Item();
                Require(Extract(duplicate) == 165, "Duplicate power did not return 165");
                Require(Read<DBInventory>(duplicate.Id) != null && Read<DBGameAccount>(account.Id).CraftItem4 == 95, "Duplicate extraction consumed inputs");

                var crown = Item("Unique_Helm_002_p1");
                var crownPower = Power("Unique_Helm_002_p1");
                Execute("CREATE FUNCTION reject_cube_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected cube regression failure'; END $$");
                Execute("CREATE TRIGGER reject_cube_write BEFORE DELETE ON items FOR EACH ROW EXECUTE FUNCTION reject_cube_write()");
                var rejected = false;
                try { Extract(crown, power: crownPower); }
                catch (TargetInvocationException e) when (e.InnerException is NHibernate.Exceptions.GenericADOException) { rejected = true; }
                Require(rejected, "Injected database failure did not reach the transaction");
                Require(Read<DBInventory>(crown.Id) != null && Read<DBGameAccount>(account.Id).CraftItem4 == 95, "Database rollback lost inputs");
                Execute("DROP TRIGGER reject_cube_write ON items");
                Require(Extract(crown, power: crownPower) == 0, "Rolled-back power was incorrectly learned");
                var newCrown = Item("Unique_Helm_002_p3");
                Require(Extract(newCrown, power: Power("Unique_Helm_002_p3")) == 165 && Read<DBInventory>(newCrown.Id) != null,
                    "Replacement versions did not share one learned power");

                foreach (var scope in new[] { (true, false, 0), (false, true, 1), (false, true, 2), (true, true, 1) })
                    Require(Extract(Item(), scope.Item1, scope.Item2, scope.Item3) == 0, "Collections leaked across mode or season");
                using (var session = factory.OpenStatelessSession())
                {
                    var collections = session.QueryOver<DBCraft>().Where(c => c.DBGameAccount.Id == account.Id).List();
                    Require(collections.Count == 5, "Expected separate normal/hardcore/season collections");
                    var normal = collections.Single(c => !c.isHardcore && !c.isSeasoned);
                    Require(((int[])Call("ReadCollection", normal)).SequenceEqual(new[] { -844626822, 334197455 }), "Collection did not persist canonical item IDs");
                }
                account = Account(); Save(account);
                Require(Extract(Item()) == 0, "Collection leaked across accounts");
                account = Account(); Save(account);
                var racingItems = new[] { Item(), Item() };
                var outcomes = Task.WhenAll(racingItems.Select(item => Task.Run(() => Extract(item)))).GetAwaiter().GetResult();
                Require(outcomes.Order().SequenceEqual(new[] { 0, 165 }), "Concurrent requests extracted the same power twice");
                Require(Read<DBGameAccount>(account.Id).CraftItem4 == 95 && racingItems.Count(item => Read<DBInventory>(item.Id) != null) == 1,
                    "Concurrent duplicate request consumed extra inputs");
            }
            finally { factoryField.SetValue(null, null); }
        }
        finally { new NpgsqlCommand($"DROP DATABASE {database}", admin).ExecuteNonQuery(); }
    }

    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
