﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Reflection;

using System.Xml.Linq;

using Terraria;
using TShockAPI;
using System.Threading.Tasks;

namespace Wolfje.Plugins.SEconomy {

    /// <summary>
    /// Seconomy for Terraria and TShock.  Copyright (C) Tyler Watson, 2013.
    /// 
    /// API Version 2.1
    /// </summary>
    [APIVersion(1, 12)]
    public class SEconomyPlugin : TerrariaPlugin {
  
        internal static readonly Performance.Profiler Profiler = new Performance.Profiler();
        static List<Economy.EconomyPlayer> economyPlayers;
        static Journal.XBankAccount _worldBankAccount;
      
        public static Configuration Configuration { get; private set; }

        static System.Timers.Timer PayRunTimer { get; set; }
        static System.Timers.Timer JournalBackupTimer { get; set; }

        public static bool BackupCanRun { get; set; }

        public static Version PluginVersion {
            get {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
        }

        public static List<Economy.EconomyPlayer> EconomyPlayers {
            get {
                return economyPlayers;
            }
        }

        #region "API Plugin Stub"
        public override string Author {
            get {
                return "Wolfje";
            }
        }

        public override string Description {
            get {
                return "Provides server-sided currency tools for servers running TShock";
            }
        }

        public override string Name {
            get {
                return "SEconomy (Milestone 1 BETA) Update 3";
            }
        }

        public override Version Version {
            get {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
        }

        #endregion

        #region "Constructors"

        public SEconomyPlugin(Main game)
            : base(game) {
            Order = 20000;

            if (!System.IO.Directory.Exists(Configuration.BaseDirectory)) {
                System.IO.Directory.CreateDirectory(Configuration.BaseDirectory);
            }
             
            economyPlayers = new List<Economy.EconomyPlayer>();

            TShockAPI.Hooks.PlayerHooks.PlayerLogin += PlayerHooks_PlayerLogin;

            Hooks.GameHooks.PostInitialize += GameHooks_PostInitialize;
            Hooks.ServerHooks.Join += ServerHooks_Join;
            Hooks.ServerHooks.Leave += ServerHooks_Leave;
            Hooks.NetHooks.GetData += NetHooks_GetData;

            Economy.EconomyPlayer.PlayerBankAccountLoaded += EconomyPlayer_PlayerBankAccountLoaded;
            Journal.XBankAccount.BankAccountFlagsChanged += BankAccount_BankAccountFlagsChanged;
            Journal.XBankAccount.BankTransferCompleted += BankAccount_BankTransferCompleted;
        }

        /// <summary>
        /// Destructor:  flush uncommitted transactions to disk before this object is cleaned up.
        /// </summary>
        ~SEconomyPlugin() {
            lock (Journal.TransactionJournal.__staticLock) {
                if (Journal.TransactionJournal.XmlJournal != null) {
                    Console.WriteLine("seconomy journal: emergency flushing journal to disk.");
                    Journal.TransactionJournal.SaveXml(Configuration.JournalPath);
                }
            }
        }

        /// <summary>
        /// Occurs when the server receives data from the client.
        /// </summary>
        void NetHooks_GetData(Hooks.GetDataEventArgs e) {

            if (e.MsgID == PacketTypes.PlayerUpdate) {
                byte playerIndex = e.Msg.readBuffer[e.Index];
                PlayerControlFlags playerState = (PlayerControlFlags)e.Msg.readBuffer[e.Index + 1];
                Economy.EconomyPlayer currentPlayer = GetEconomyPlayerSafe(playerIndex);

                //The idea behind this logic is that IdleSince resets to now any time the server an action from the client.
                //If the client never updates, or updates to 0 (Idle) then "IdleSince" never changes.
                //When you want to get the amount of time the player has been idle, just subtract it from DateTime.Now
                //And voila, you get a TimeSpan with how long the user has been idle for.
                if (playerState != PlayerControlFlags.Idle) {
                    currentPlayer.IdleSince = DateTime.Now;
                }

                currentPlayer.LastKnownState = playerState;
            }
        }

        protected override void Dispose(bool disposing) {

            if (disposing) {
                TShockAPI.Hooks.PlayerHooks.PlayerLogin -= PlayerHooks_PlayerLogin;

                Hooks.ServerHooks.Join -= ServerHooks_Join;
                Hooks.NetHooks.GetData -= NetHooks_GetData;
                Hooks.ServerHooks.Leave -= ServerHooks_Leave;

                Economy.EconomyPlayer.PlayerBankAccountLoaded -= EconomyPlayer_PlayerBankAccountLoaded;
                Journal.XBankAccount.BankAccountFlagsChanged -= BankAccount_BankAccountFlagsChanged;
                Journal.XBankAccount.BankTransferCompleted -= BankAccount_BankTransferCompleted;
                Hooks.GameHooks.PostInitialize -= GameHooks_PostInitialize;

                Console.WriteLine("seconomy journal: emergency flushing journal to disk.");
                Journal.TransactionJournal.SaveXml(Configuration.JournalPath);

                economyPlayers = null;

            }

            base.Dispose(disposing);
        }

        #endregion

        /// <summary>
        /// Initialization point for the Terrraria API
        /// </summary>
        public override void Initialize() {
            Configuration = Configuration.LoadConfigurationFromFile(Configuration.BaseDirectory + System.IO.Path.DirectorySeparatorChar + "SEconomy.config.json");

            try {
                Journal.TransactionJournal.LoadFromXmlFile(Configuration.JournalPath);
            } catch {
                TShockAPI.Log.ConsoleError("SEconomy: xml initialization failed.");
                throw;
            }

            //Initialize the command interface
            ChatCommands.Initialize();

            Log.ConsoleInfo(string.Format("seconomy xml: backing up journal every {0} minutes.", Configuration.JournalBackupMinutes));
            JournalBackupTimer = new System.Timers.Timer(Configuration.JournalBackupMinutes * 60000);
            JournalBackupTimer.Elapsed += JournalBackupTimer_Elapsed;
            JournalBackupTimer.Start();
        }

        

        #region "Event Handlers"

        /// <summary>
        /// Occurs when the transaction journal needs to be backed up.
        /// </summary>
        void JournalBackupTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e) {
            if (BackupCanRun) {
                Journal.TransactionJournal.BackupJournalAsync();
            }
        }

        void GameHooks_PostInitialize() {

            //this is the pay run timer.
            //The timer event fires when it's time to do a pay run to the online players
            //This event fires in PayRunTimer_Elapsed
            if (Configuration.PayIntervalMinutes > 0) {
                PayRunTimer = new System.Timers.Timer(Configuration.PayIntervalMinutes * 60000);
                PayRunTimer.Elapsed += PayRunTimer_Elapsed;
                PayRunTimer.Start();
            }

            WorldAccount = Journal.TransactionJournal.EnsureWorldAccountExists();

            SEconomyPlugin.BackupCanRun = true;
        }


        /// <summary>
        /// Fires when a player's bank account is loaded from the database.
        /// </summary>
        void EconomyPlayer_PlayerBankAccountLoaded(object sender, EventArgs e) {
            Economy.EconomyPlayer ePlayer = sender as Economy.EconomyPlayer;

            if (ePlayer.BankAccount != null) {
                if (ePlayer.BankAccount.IsAccountEnabled) {
                    ePlayer.TSPlayer.SendInfoMessage(string.Format("You have {0}", ePlayer.BankAccount.Balance.ToLongString(true)));
                } else {
                    ePlayer.TSPlayer.SendInfoMessage("Your bank account is disabled.");
                }
            }
        }

        /// <summary>
        /// Synchronization object to disable reentrancy into the transfer completed handler
        /// </summary>
        public static readonly object __transferCompleteLock = new object();

        /// <summary>
        /// Synchronizarion object to disable reentracy into the "get account" statics
        /// </summary>
        static object __accountSafeLock = new object();

        /// <summary>
        /// Occurs when a bank transfer completes.
        /// </summary>
        void BankAccount_BankTransferCompleted(object sender, Journal.BankTransferEventArgs e) {
            //this is pretty balls too, but will do for now.

            lock (__transferCompleteLock) {
                if ((e.TransferOptions & Journal.BankAccountTransferOptions.SuppressDefaultAnnounceMessages) == Journal.BankAccountTransferOptions.SuppressDefaultAnnounceMessages) {
                    return;
                } else if (e.ReceiverAccount != null) {

                    //Player died from PvP
                    if ((e.TransferOptions & Journal.BankAccountTransferOptions.MoneyFromPvP) == Journal.BankAccountTransferOptions.MoneyFromPvP) {
                        if ((e.TransferOptions & Journal.BankAccountTransferOptions.AnnounceToReceiver) == Journal.BankAccountTransferOptions.AnnounceToReceiver) {
                            e.ReceiverAccount.Owner.TSPlayer.SendMessage(string.Format("You killed {0} and gained {1}.", e.SenderAccount.Owner.TSPlayer.Name, e.Amount.ToLongString()), Color.Orange);
                        }
                        if ((e.TransferOptions & Journal.BankAccountTransferOptions.AnnounceToSender) == Journal.BankAccountTransferOptions.AnnounceToSender) {
                            e.SenderAccount.Owner.TSPlayer.SendMessage(string.Format("{0} killed you and you lost {1}.", e.ReceiverAccount.Owner.TSPlayer.Name, e.Amount.ToLongString()), Color.Orange);
                        }

                        //P2P transfers, both the sender and the reciever get notified.
                    } else if ((e.TransferOptions & Journal.BankAccountTransferOptions.IsPlayerToPlayerTransfer) == Journal.BankAccountTransferOptions.IsPlayerToPlayerTransfer) {
                        if ((e.TransferOptions & Journal.BankAccountTransferOptions.AnnounceToReceiver) == Journal.BankAccountTransferOptions.AnnounceToReceiver && e.ReceiverAccount != null && e.ReceiverAccount.Owner != null) {
                            e.ReceiverAccount.Owner.TSPlayer.SendMessage(string.Format("You received {0} from {1}. Transaction # {2}", e.Amount.ToLongString(), e.SenderAccount.Owner != null ? e.SenderAccount.Owner.TSPlayer.Name : "The server", e.TransactionID), Color.Orange);
                        }
                        if ((e.TransferOptions & Journal.BankAccountTransferOptions.AnnounceToSender) == Journal.BankAccountTransferOptions.AnnounceToSender && e.SenderAccount.Owner != null) {
                            e.SenderAccount.Owner.TSPlayer.SendMessage(string.Format("You sent {0} to {1}. Transaction # {2}", e.Amount.ToLongString(), e.ReceiverAccount.Owner.TSPlayer.Name, e.TransactionID), Color.Orange);
                        }

                        //Everything else, including world to player, and player to world.
                    } else {
                        string moneyVerb = "";

                        if ((e.TransferOptions & Journal.BankAccountTransferOptions.AnnounceToSender) == Journal.BankAccountTransferOptions.AnnounceToSender && e.SenderAccount.Owner != null) {

                            if ((e.TransferOptions & Journal.BankAccountTransferOptions.IsPayment) == Journal.BankAccountTransferOptions.IsPayment) {
                                moneyVerb = "paid";
                            } else if (e.Amount > 0) {
                                moneyVerb = "gained";
                            } else {
                                moneyVerb = "lost";
                            }

                            e.SenderAccount.Owner.TSPlayer.SendMessage(string.Format("You {0} {1}", moneyVerb, e.Amount.ToLongString()), Color.Orange);
                        }
                        if ((e.TransferOptions & Journal.BankAccountTransferOptions.AnnounceToReceiver) == Journal.BankAccountTransferOptions.AnnounceToReceiver && e.ReceiverAccount.Owner != null) {
                            if ((e.TransferOptions & Journal.BankAccountTransferOptions.IsPayment) == Journal.BankAccountTransferOptions.IsPayment) {
                                moneyVerb = "got paid";
                            } else if (e.Amount > 0) {
                                moneyVerb = "gained";
                            } else {
                                moneyVerb = "lost";
                            }

                            e.ReceiverAccount.Owner.TSPlayer.SendMessage(string.Format("You {0} {1}", moneyVerb, e.Amount.ToLongString()), Color.Orange);
                        }
                    }
                } else if (e.TransferSucceeded == true) {
                    TShockAPI.Log.ConsoleError("seconomy error: Bank account transfer completed without a receiver: ID " + e.TransactionID);
                }
            }
        }

        /// <summary>
        /// Occurs when a player's bank account flags change
        /// </summary>
        void BankAccount_BankAccountFlagsChanged(object sender, Journal.BankAccountChangedEventArgs e) {
            Journal.XBankAccount bankAccount = sender as Journal.XBankAccount;
            Economy.EconomyPlayer player = GetEconomyPlayerByBankAccountNameSafe(bankAccount.UserAccountName);


            //You can technically make payments to anyone even if they are offline.
            //This serves as a basic online check as we don't give a fuck about informing
            //an offline person that their account has been disabled or not.
            if (player != null) {
                bool enabled = (e.NewFlags & Journal.BankAccountFlags.Enabled) == Journal.BankAccountFlags.Enabled;

                TSPlayer caller = TShock.Players[e.CallerID];
                if (player.TSPlayer.Name == caller.Name) {
                    player.TSPlayer.SendInfoMessageFormat("bank: Your bank account has been {0}d.", enabled ? "enable" : "disable");
                } else {
                    player.TSPlayer.SendInfoMessageFormat("bank: {1} {0}d your account.", enabled ? "enable" : "disable", caller.Name);
                }
            }

        }

        /// <summary>
        /// Fires when a player leaves.
        /// </summary>
        void ServerHooks_Leave(int PlayerIndex) {
            Economy.EconomyPlayer ePlayer = GetEconomyPlayerSafe(PlayerIndex);

            //Lock players, deleting needs to block to avoid iterator crashes and race conditions
            lock (__accountSafeLock) {
                economyPlayers.Remove(ePlayer);
            }
        }

        /// <summary>
        /// Fires when a player joins
        /// </summary>
        void ServerHooks_Join(int playerId, System.ComponentModel.HandledEventArgs e) {
            //Add economy player wrapper to the static list of players.
            lock (__accountSafeLock) {
                Economy.EconomyPlayer player = new Economy.EconomyPlayer(playerId);

                economyPlayers.Add(player);

                //if the user belongs to group superadmin we can assume they are trusted and attempt to load a bank account via name.
                //everyone else has to login
                if (player.TSPlayer.Group is TShockAPI.SuperAdminGroup) {
                    //player.LoadBankAccountByPlayerNameAsync();
                }
            }
            
        }

        /// <summary>
        /// Fires when a user logs in.
        /// </summary>
        void PlayerHooks_PlayerLogin(TShockAPI.Hooks.PlayerLoginEventArgs e) {
            Economy.EconomyPlayer ePlayer = GetEconomyPlayerSafe(e.Player.Index);

            //Ensure a bank account for the economy player exists, and asynchronously load it.
            ePlayer.EnsureBankAccountExists();
        }


        /// <summary>
        /// Occurs when a player online payment needs to occur.
        /// </summary>
        void PayRunTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e) {
            lock (__accountSafeLock) {

                if (Configuration.PayIntervalMinutes > 0 && !string.IsNullOrEmpty(Configuration.IntervalPayAmount)) {
                    Money payAmount;

                    if (Money.TryParse(Configuration.IntervalPayAmount, out payAmount)) {
                        if (payAmount > 0) {
                            foreach (Economy.EconomyPlayer ep in economyPlayers) {
                                //if the time since the player was idle is less than or equal to the configuration idle threshold
                                //then the player is considered not AFK.
                                if (ep.TimeSinceIdle.TotalMinutes <= Configuration.IdleThresholdMinutes && ep.BankAccount != null) {
                                    //Pay them from the world account
                                    WorldAccount.TransferToAsync(ep.BankAccount, payAmount, Journal.BankAccountTransferOptions.AnnounceToReceiver);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region "Static APIs"


   
        /// <summary>
        /// Gets an economy-enabled player by their player name. 
        /// </summary>
        public static Economy.EconomyPlayer GetEconomyPlayerByBankAccountNameSafe(string Name) {
            lock (__accountSafeLock) {
                return economyPlayers.FirstOrDefault(i => (i.BankAccount != null) && i.BankAccount.UserAccountName == Name);
            }
        }

        /// <summary>
        /// Gets an economy-enabled player by their player name. 
        /// </summary>
        public static Economy.EconomyPlayer GetEconomyPlayerSafe(string Name) {
            lock (__accountSafeLock) {
                return economyPlayers.FirstOrDefault(i => i.TSPlayer.Name == Name);
            }
        }

        /// <summary>
        /// Gets an economy-enabled player by their index.  This method is thread-safe.
        /// </summary>
        public static Economy.EconomyPlayer GetEconomyPlayerSafe(int id) {
            Economy.EconomyPlayer p = null;

            if (id < 0) {
                //make a shitty faux account with the world account so that bank works from the console.
                p = new Economy.EconomyPlayer(-1);
                p.BankAccount = SEconomyPlugin.WorldAccount;
            } else {
                lock (__accountSafeLock) {
                    foreach (Economy.EconomyPlayer ep in economyPlayers) {
                        if (ep.Index == id) {
                            p = ep;
                        }
                    }
                }
            }

          

            return p;
        }

        /// <summary>
        /// Gets the world bank account (system account) for paying players.
        /// </summary>
        public static Journal.XBankAccount WorldAccount {
            get {
                return _worldBankAccount;
            }

            internal set {
                _worldBankAccount = value;

                if (_worldBankAccount != null) {
                    _worldBankAccount.SyncBalanceAsync().ContinueWith((task) => {
                        Log.ConsoleInfo(string.Format("SEconomy: world account: paid {0} to players.", _worldBankAccount.Balance.ToLongString()));
                    });
                }
            }
        }


        #endregion


        /// <summary>
        /// Reflects on a private method.  Can remove this if TShock opens up a bit more of their API publicly
        /// </summary>
        public static T CallPrivateMethod<T>(Type type, bool staticMember, string name, params object[] param) {
            BindingFlags flags = BindingFlags.NonPublic;
            if (staticMember) {
                flags |= BindingFlags.Static;
            } else {
                flags |= BindingFlags.Instance;
            }
            MethodInfo method = type.GetMethod(name, flags);
            return (T)method.Invoke(staticMember ? null : type, param);
        }

        /// <summary>
        /// Reflects on a private instance member of a class.  Can remove this if TShock opens up a bit more of their API publicly
        /// </summary>
        public static T GetPrivateField<T>(Type type, object instance, string name, params object[] param) {
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo field = type.GetField(name, flags) as FieldInfo;

            return (T)field.GetValue(instance);
        }

        public static void FillWithSpaces(ref string Input) {
            int i = Input.Length;
            StringBuilder sb = new StringBuilder(Input);

            do {
                sb.Append(" ");
                i++;
            } while (i < Console.WindowWidth - 1);

            Input = sb.ToString();
        }


    }
}
