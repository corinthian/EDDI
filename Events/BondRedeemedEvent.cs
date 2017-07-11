﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EddiEvents
{
    public class BondRedeemedEvent : Event
    {
        public const string NAME = "Bond redeemed";
        public const string DESCRIPTION = "Triggered when you redeem a combat bond";
        public const string SAMPLE = @"{ ""timestamp"":""2016-12-29T10:10:11Z"", ""event"":""RedeemVoucher"", ""Type"":""CombatBond"",""Amount"":2000,""Factions"":[{""Faction"":""The Pilots Federation"",""Amount"":1000},{""Faction"":""The Dark Wheel"",""Amount"":500},{""Faction"":""Los Chupacabras"",""Amount"":500}]}";

        public static Dictionary<string, string> VARIABLES = new Dictionary<string, string>();

        static BondRedeemedEvent()
        {
            VARIABLES.Add("rewards", "The rewards obtained broken down by faction");
            VARIABLES.Add("amount", "The amount rewarded (after any broker fees)");
        }

        [JsonProperty("rewards")]
        public List<Reward> rewards { get; private set; }

        [JsonProperty("amount")]
        public long amount { get; private set; }

        public BondRedeemedEvent(DateTime timestamp, List<Reward> rewards, long amount) : base(timestamp, NAME)
        {
            this.rewards = rewards;
            this.amount = amount;
        }
    }
}
