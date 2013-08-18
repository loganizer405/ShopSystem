using System;
using System.Collections.Generic;
using Terraria;
using Hooks;
using TShockAPI;
using TShockAPI.DB;
using MySql.Data.MySqlClient;
using System.Threading;
using System.ComponentModel;
using System.IO;
using System.Data;
using System.Linq;

namespace ShopSystem
{
    public class SqlManager
    {

        private static IDbConnection database;
        private SqlManager()
        {
        }
        public static void EnsureTableExists(IDbConnection db)
        {
            database = db;

            var table = new SqlTable("ShopSystem",
                new SqlColumn("Name", MySqlDbType.Text),
                new SqlColumn("Copper", MySqlDbType.Int32),
                new SqlColumn("Silver", MySqlDbType.Int32),
                new SqlColumn("Gold", MySqlDbType.Int32),
                new SqlColumn("ForSale", MySqlDbType.Int32),
                new SqlColumn("MaxStack", MySqlDbType.Int32)
            );
            var creator = new SqlTableCreator(db,
             db.GetSqlType() == SqlType.Sqlite
             ? (IQueryBuilder)new SqliteQueryCreator()
             : new MysqlQueryCreator());

            creator.EnsureExists(table);
            

        }
        public static bool InitializeTable()
        {
            var SQLEditor = new SqlTableEditor(TShock.DB, TShock.DB.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            try
            {
               if (SQLEditor.ReadColumn("ShopSystem", "Name", new List<SqlValue>()).Count < 1)
                {
                    Console.WriteLine("Writing item list for ShopSystem (This may take a while, please be patient)...");
                        for (int k = 1; k < 604; k++)
                        {
                            if (k == 269 || k == 270 || k == 271)//this is a tshock bug
                            {
                            }
                            else
                            {
                                Item item = TShockAPI.TShock.Utils.GetItemById(k);
                                int value = item.value;
                                int copper;
                                int silver;
                                int gold;
                                if (value % 10 != 0)
                                {
                                    copper = Convert.ToInt32(item.value / 1.5);
                                    silver = 0;
                                    gold = 0;
                                }
                                gold = value / 1000;
                                value = value - gold * 1000;
                                silver = value / 10;
                                value = value - silver * 10;
                                copper = Convert.ToInt32(value * 1.5);
                                database.Query("INSERT INTO ShopSystem (Name, Copper, Silver, Gold, ForSale, MaxStack)" +
                                    " VALUES (@0, @1, @2, @3, 1, @4)", item.name, copper, silver, gold, item.maxStack);
                            }
                        }
                        Console.WriteLine("Wrote item list to SQL database successfully.");
                        Thread.Sleep(1000);
                    }
                return true;
           }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to write to SQL Database!");
                Log.Error("Write to SQL exception:(ShopSystem)");
                Log.Error(ex.ToString());
                return false;

            }
        }
        public static bool GetInfo(string name, out int copper, out int silver, out int gold, out int maxstack, out bool forsale)
        {
            try
            {
                using (var reader = database.QueryReader("SELECT * FROM ShopSystem WHERE Name = @0", name))
                {
                    if (reader.Read())
                    {
                        copper = reader.Get<int>("Copper");
                        silver = reader.Get<int>("Silver");
                        gold = reader.Get<int>("Gold");
                        maxstack = reader.Get<int>("MaxStack");

                        if (reader.Get<int>("ForSale") == 1)
                            forsale = true;
                        else
                            forsale = false;
                        return true;
                    }
                    else
                    {
                        copper = 0;
                        silver = 0;
                        gold = 0;
                        maxstack = 0;
                        forsale = false;
                        return false;
                        throw (new Exception("Failed to GetInfo on database(Database could not be read)"));
                    }
                    
                }
             }
            catch (Exception ex)
            {
                Log.Error("Read SQL exception:(ShopSystem)");
                Log.Error(ex.ToString());
                copper = 0;
                silver = 0;
                gold = 0;
                maxstack = 1;
                forsale = false;
                return false;

            }
        }

    }
}