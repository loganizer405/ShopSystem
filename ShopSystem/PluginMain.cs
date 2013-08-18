using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.IO;
using System.Timers;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using Terraria;
using TShockAPI;
using TShockAPI.DB;
using Hooks;
using System.Reflection;
using System.Web;
using Wolfje.Plugins.SEconomy;
using Wolfje.Plugins.SEconomy.Economy;
using Wolfje.Plugins.SEconomy.Forms;
using Wolfje.Plugins.SEconomy.Journal;
using Wolfje.Plugins.SEconomy.VaultExModule;

namespace ShopSystem
{
    [APIVersion(1, 12)]
    public class ShopSystem : TerrariaPlugin
    {
        public override string Author
        {
            get { return "Loganizer"; }
        }
        public override string Description
        {
            get { return "Adds a shop system for SEconomy"; }
        }

        public override string Name
        {
            get { return "ShopSystem"; }
        }

        public override Version Version
        {
            get { return new Version("1.0"); }
        }
        public ShopSystem(Main game)
            : base(game)
        {
            Order = 10;
        }
        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("shop.buy", BuyItem, "buy"));
            Commands.ChatCommands.Add(new Command("shop.checkprice", CheckPrice, "checkprice"));
            SqlManager.EnsureTableExists(TShock.DB);
            SqlManager.InitializeTable();
        }
        void CheckPrice(CommandArgs args)
        {
            if (args.Parameters.Count < 1 || args.Parameters.Count > 2)
            {
                args.Player.SendErrorMessage("Invalid syntax! Proper syntax: /checkprice <itemname> [stack]");
                return;
            }
            int copper;
            int silver;
            int gold;
            int maxstack;
            bool forsale;
            int stack = 1;
            var items = TShock.Utils.GetItemByIdOrName(args.Parameters[0]);
            if (args.Parameters.Count == 2)
            {
                if (args.Parameters[1] != "" && args.Parameters[1] != null)
                {
                    try
                    {
                        stack = Convert.ToInt32(args.Parameters[1]);
                    }
                    catch
                    {
                        stack = 1;
                    }
                }
            }
            else
                stack = 1;

             if (items.Count > 1)
             {
                 args.Player.SendErrorMessage(string.Format("More than one ({0}) item matched!", items.Count));
             }
             if (items.Count == 1)
             {
                 if (SqlManager.GetInfo(items[0].name, out copper, out silver, out gold, out maxstack, out forsale))
                 {
                     if (stack > maxstack)
                         stack = maxstack;
                     if (stack == 0)
                         stack = 1;
                     if (forsale == false)
                     {
                         args.Player.SendErrorMessage("That item is not for sale.");
                     }

                     else
                     {
                         if (SEconomyPlugin.Configuration.MoneyConfiguration.UseQuadrantNotation == false)
                         {
                             int price = ((gold * 10000) + (silver * 100) + copper) * stack;
                             args.Player.SendInfoMessage(stack + " " + items[0].name + "(s) is worth " +
                                 price + " " + SEconomyPlugin.Configuration.MoneyConfiguration.MoneyNamePlural + ".");
                         }
                         else
                         {
                             args.Player.SendInfoMessage(stack + " " + items[0].name + "(s) is worth " + copper * stack + " copper, " + silver * stack +
                                 " silver, and " + gold * stack + " gold.");
                         }
                         if (TShock.Regions.GetRegionByName("shop").Name != "" || TShock.Regions.GetRegionByName("shop") != null)
                         {
                             args.Player.SendInfoMessage("Note: you must be at /warp shop to purchase items.");
                         }
                     }
                 }
                 else
                     args.Player.SendErrorMessage("Something went wrong. Please try again later.");

             }
             else
             {
                 args.Player.SendErrorMessage("Invalid item type!");
             }
        }
        void BuyItem(CommandArgs args)
        {
            if (args.Parameters.Count < 1 || args.Parameters.Count > 2)
            {
                args.Player.SendErrorMessage("Invalid syntax! Proper syntax: /buy <itemname> [stack]");
                return;
            }
            if (TShock.Regions.GetRegionByName("shop").Name != "" || TShock.Regions.GetRegionByName("shop") != null)
            {
                string currentregionlist = "";
                var currentregion = TShock.Regions.InAreaRegionName(args.Player.TileX, args.Player.TileY);
                if (currentregion.Count > 0)
                    currentregionlist = string.Join(",", currentregion.ToArray());

                if (!currentregionlist.Contains("shop"))
                {
                    args.Player.SendErrorMessage("You must be at /warp shop to purchase items.");
                    return;
                }
            }
            int stack = 1;
            long[] coins = new long[4];
            EconomyPlayer player = SEconomyPlugin.GetEconomyPlayerSafe(args.Player.Name);
            Money money;
            money = player.BankAccount.Balance;
            if(args.Parameters.Count == 2)
            {
                try
                {
                    stack = Convert.ToInt32(args.Parameters[1]);
                }
                catch
                {
                    stack = 1;
                }
            }
        
            else
                stack = 1;
            
        
            var items = TShock.Utils.GetItemByIdOrName(args.Parameters[0]);
            if (items.Count > 1)
            {
                args.Player.SendErrorMessage(string.Format("More than one ({0}) item matched!", items.Count));
            }
            if (items.Count == 1)
            { 
                if (args.Player.InventorySlotAvailable || items[0].name.Contains("Coin"))
                {
                    try
                    {
                        int copper;
                        int silver;
                        int gold;
                        int maxstack;
                        bool forsale;

                        if (SqlManager.GetInfo(items[0].name, out copper, out silver, out gold, out maxstack, out forsale))
                        {

                            if (stack > maxstack)
                            {
                                stack = maxstack;
                            }
                            int price;

                            price = ((gold * 10000) + (silver * 100) + copper) * stack;
                            if (SEconomyPlugin.Configuration.MoneyConfiguration.UseQuadrantNotation == true)
                            {

                                int copperb = Convert.ToInt32(player.BankAccount.Balance.Copper);
                                int silverb = Convert.ToInt32(player.BankAccount.Balance.Silver);
                                int goldb = Convert.ToInt32(player.BankAccount.Balance.Gold);
                                int platb = Convert.ToInt32(player.BankAccount.Balance.Platinum);
                                if (((platb * 1000000) + (goldb * 10000) + (silverb * 100) + copper) < price && price != 0)
                                {
                                    args.Player.SendErrorMessage("You do not have enough money to buy " + stack + " " + items[0].name + "(s).");
                                    return;
                                }
                                else
                                {
                                    if (price != 0)
                                    {
                                        player.BankAccount.TransferTo(SEconomyPlugin.WorldAccount, price, BankAccountTransferOptions.None);
                                    }
                                    args.Player.GiveItemCheck(items[0].type, items[0].name, items[0].width, items[0].height, stack);
                                    args.Player.SendSuccessMessage("Bought " + stack + " " + items[0].name + "(s) for " + 
                                        gold + " " + SEconomyPlugin.Configuration.MoneyConfiguration.Quadrant1FullName +
                                        silver + " " + SEconomyPlugin.Configuration.MoneyConfiguration.Quadrant2FullName + " and " +
                                        copper + " " + SEconomyPlugin.Configuration.MoneyConfiguration.Quadrant3FullName + ".");
                                    Log.Info(args.Player.Name + " bought " + stack + " " + items[0].name + "(s) for " +
                                        gold + " " + SEconomyPlugin.Configuration.MoneyConfiguration.Quadrant1FullName +
                                        silver + " " + SEconomyPlugin.Configuration.MoneyConfiguration.Quadrant2FullName + " and " +
                                        copper + " " + SEconomyPlugin.Configuration.MoneyConfiguration.Quadrant3FullName + ".");
                                }
                            }
                            else
                            {
                                if (player.BankAccount.Balance.Value < price && price != 0)
                                {
                                    args.Player.SendErrorMessage("You do not have enough money to buy " + stack + " " + items[0].name + "(s).");
                                    return;
                                }
                                if (price != 0)
                                {
                                    player.BankAccount.TransferTo(SEconomyPlugin.WorldAccount, price, BankAccountTransferOptions.None);
                                }
                                args.Player.GiveItemCheck(items[0].type, items[0].name, items[0].width, items[0].height, stack);
                                args.Player.SendSuccessMessage("Bought " + stack + " " + items[0].name + "(s) for " +
                                    price + " " + SEconomyPlugin.Configuration.MoneyConfiguration.MoneyNamePlural + ".");
                                Log.Info(args.Player.Name + " bought " + stack + " " + items[0].name + "(s) for " +
                                price + " " + SEconomyPlugin.Configuration.MoneyConfiguration.MoneyNamePlural + ".");
                            }

                            if (forsale == false)
                            {
                                args.Player.SendErrorMessage("That item is not for sale.");
                                return;
                            }
                        }
                        else
                        {
                            args.Player.SendErrorMessage("Something went wrong. Please try again later.");
                        }
                    }
                    catch(Exception ex)
                    {
                        args.Player.SendErrorMessage("Failed to buy item(s).");
                        Log.Error("Failed to give item to " + args.Player.Name + ". (ShopSystem)");
                        Log.Error(ex.ToString());
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("You don't have any free slots!");
                }
            }
            else
            {
                args.Player.SendErrorMessage("Invalid item type!");
            }
        }
    }
}