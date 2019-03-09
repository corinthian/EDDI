﻿using Eddi;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiEvents;
using EddiStarMapService;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Threading;
using Utilities;

namespace EddiCrimeMonitor
{
    /**
     * Monitor claims, fines, and bounties for the current ship
     */
    public class CrimeMonitor : EDDIMonitor
    {
        private bool running;

        // Observable collection for us to handle changes
        public ObservableCollection<FactionRecord> criminalrecord { get; private set; }
        public long claims;
        public long fines;
        public long bounties;
        private DateTime updateDat;

        private static readonly object recordLock = new object();
        public event EventHandler RecordUpdatedEvent;

        public string MonitorName()
        {
            return "Crime monitor";
        }

        public string LocalizedMonitorName()
        {
            return Properties.CrimeMonitor.crime_monitor_name;
        }

        public string MonitorVersion()
        {
            return "1.0.0";
        }

        public string MonitorDescription()
        {
            return Properties.CrimeMonitor.crime_monitor_desc;
        }

        public bool IsRequired()
        {
            return true;
        }

        public CrimeMonitor()
        {
            criminalrecord = new ObservableCollection<FactionRecord>();
            BindingOperations.CollectionRegistering += Record_CollectionRegistering;
            initializeCrimeMonitor();
        }

        public void initializeCrimeMonitor(CrimeMonitorConfiguration configuration = null)
        {
            readRecord(configuration);
            Logging.Info("Initialised " + MonitorName() + " " + MonitorVersion());
        }

        private void Record_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
        {
            if (Application.Current != null)
            {
                // Synchronize this collection between threads
                BindingOperations.EnableCollectionSynchronization(criminalrecord, recordLock);
            }
            else
            {
                // If started from VoiceAttack, the dispatcher is on a different thread. Invoke synchronization there.
                Dispatcher.CurrentDispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(criminalrecord, recordLock); });
            }
        }
        public bool NeedsStart()
        {
            // We don't actively do anything, just listen to events
            return true;
        }

        public void Start()
        {
            _start();
        }

        public void Stop()
        {
            running = false;
        }

        public void Reload()
        {
            readRecord();
            Logging.Info("Reloaded " + MonitorName() + " " + MonitorVersion());

        }

        public void _start()
        {
            running = true;

            while (running)
            {
                List<FactionRecord> recordList;
                lock (recordLock)
                {
                    recordList = criminalrecord.ToList();
                }

                Thread.Sleep(5000);
            }
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        public void EnableConfigBinding(MainWindow configWindow)
        {
            configWindow.Dispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(criminalrecord, recordLock); });
        }

        public void DisableConfigBinding(MainWindow configWindow)
        {
            configWindow.Dispatcher.Invoke(() => { BindingOperations.DisableCollectionSynchronization(criminalrecord); });
        }

        public void HandleProfile(JObject profile)
        {
        }

        public void PostHandle(Event @event)
        {
        }

        public void PreHandle(Event @event)
        {
            Logging.Debug("Received event " + JsonConvert.SerializeObject(@event));

            // Handle the events that we care about
            if (@event is BondAwardedEvent)
            {
                handleBondAwardedEvent((BondAwardedEvent)@event);
            }
            else if (@event is BondRedeemedEvent)
            {
                handleBondRedeemedEvent((BondRedeemedEvent)@event);
            }
            else if (@event is BountyAwardedEvent)
            {
                handleBountyAwardedEvent((BountyAwardedEvent)@event);
            }
            else if (@event is BountyIncurredEvent)
            {
                handleBountyIncurredEvent((BountyIncurredEvent)@event);
            }
            else if (@event is BountyPaidEvent)
            {
                handleBountyPaidEvent((BountyPaidEvent)@event);
            }
            else if (@event is BountyRedeemedEvent)
            {
                handleBountyRedeemedEvent((BountyRedeemedEvent)@event);
            }
            else if (@event is FineIncurredEvent)
            {
                handleFineIncurredEvent((FineIncurredEvent)@event);
            }
            else if (@event is FinePaidEvent)
            {
                handleFinePaidEvent((FinePaidEvent)@event);
            }
            else if (@event is PowerSalaryClaimedEvent)
            {
                handlePowerSalaryClaimedEvent((PowerSalaryClaimedEvent)@event);
            }
            else if (@event is PowerVoucherReceivedEvent)
            {
                handlePowerVoucherReceivedEvent((PowerVoucherReceivedEvent)@event);
            }
        }

        private void handleBondAwardedEvent(BondAwardedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleBondAwardedEvent(@event);
                writeRecord();
            }
        }

        private void _handleBondAwardedEvent(BondAwardedEvent @event)
        {
            FactionRecord record = GetRecordWithFaction(@event.awardingfaction);
            if (record == null)
            {
                record = AddRecord(@event.awardingfaction);
            }
            record.claims += @event.reward;
        }

        private void handleBondRedeemedEvent(BondRedeemedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleBondRedeemedEvent(@event))
                {
                    writeRecord();
                }
            }
        }

        private bool _handleBondRedeemedEvent(BondRedeemedEvent @event)
        {
            bool update = false;

            foreach (Reward reward in @event.rewards.ToList())
            {
                FactionRecord record = GetRecordWithFaction(reward.faction);
                if (record != null)
                {
                    decimal amount = reward.amount * (1 + (@event.brokerpercentage ?? 0) / 100);
                    record.claims -= (long)amount;
                    RemoveRecord(record);
                    update = true;
                }
            }
            return update;
        }

        private void handleBountyAwardedEvent(BountyAwardedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleBountyAwardedEvent(@event);
                writeRecord();
            }
        }

        private void _handleBountyAwardedEvent(BountyAwardedEvent @event)
        {
            // 20% bonus for Arissa Lavigny-Duval 'controlled' and 'exploited' systems
            StarSystem currentSystem = EDDI.Instance?.CurrentStarSystem;
            currentSystem = LegacyEddpService.SetLegacyData(currentSystem, true, false, false);
            double bonus = currentSystem.power == "Arissa Lavigny-Duval" ? 1.2 : 1.0;

            foreach (Reward reward in @event.rewards.ToList())
            {
                FactionRecord record = GetRecordWithFaction(reward.faction);
                if (record == null)
                {
                    record = AddRecord(reward.faction);
                }
                double amount = (double)reward.amount * bonus;
                record.claims += (long)amount;
            }
        }

        private void handleBountyIncurredEvent(BountyIncurredEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleBountyIncurredEvent(@event);
                writeRecord();
            }
        }

        private void _handleBountyIncurredEvent(BountyIncurredEvent @event)
        {
            int shipId = EDDI.Instance?.CurrentShip?.LocalId ?? 0;
            Crime crime = Crime.FromEDName(@event.crime);
            string currentSystem = EDDI.Instance?.CurrentStarSystem?.name;
            CrimeReport report = new CrimeReport(@event.timestamp, true, shipId, crime, currentSystem, @event.bounty);
            AddCrimeReport(@event.faction, report);
        }

        private void handleBountyPaidEvent(BountyPaidEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleBountyPaidEvent(@event))
                {
                    writeRecord();
                }
            }
        }

        private bool _handleBountyPaidEvent(BountyPaidEvent @event)
        {
            return RemoveCrimeReport(@event.faction, @event.shipid, true);
        }

        private void handleBountyRedeemedEvent(BountyRedeemedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleBountyRedeemedEvent(@event))
                {
                    writeRecord();
                }
            }
        }

        private bool _handleBountyRedeemedEvent(BountyRedeemedEvent @event)
        {
            bool update = false;
            foreach (Reward reward in @event.rewards.ToList())
            {
                FactionRecord record = GetRecordWithFaction(reward.faction);
                if (record != null)
                {
                    record.claims -= (long)reward.amount;
                    RemoveRecord(record);
                    update = true;
                }
            }
            return update;
        }

        private void handleFineIncurredEvent(FineIncurredEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleFineIncurredEvent(@event);
                writeRecord();
            }
        }

        private void _handleFineIncurredEvent(FineIncurredEvent @event)
        {
            int shipId = EDDI.Instance?.CurrentShip?.LocalId ?? 0;
            Crime crime = Crime.FromEDName(@event.crime);
            string currentSystem = EDDI.Instance?.CurrentStarSystem?.name;
            CrimeReport report = new CrimeReport(@event.timestamp, false, shipId, crime, currentSystem, @event.fine);
            AddCrimeReport(@event.faction, report);
        }

        private void handleFinePaidEvent(FinePaidEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleFinePaidEvent(@event))
                {
                    writeRecord();
                }
            }
        }

        private bool _handleFinePaidEvent(FinePaidEvent @event)
        {
            if (@event.allfines)
            {
                bool update = false;
                Station station = EDDI.Instance?.CurrentStation;
                foreach (Faction faction in EDDI.Instance?.CurrentStarSystem?.factions)
                {
                    if (RemoveCrimeReport(faction.name, @event.shipid, false)) { update = true; }
                }
                return update;
            }
            return RemoveCrimeReport(@event.faction, @event.shipid, false);
        }

        private void handlePowerSalaryClaimedEvent(PowerSalaryClaimedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handlePowerSalaryClaimedEvent(@event))
                {
                    writeRecord();
                }
            }
        }

        private bool _handlePowerSalaryClaimedEvent(PowerSalaryClaimedEvent @event)
        {
            bool update = false;

            return update;
        }

        private void handlePowerVoucherReceivedEvent(PowerVoucherReceivedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handlePowerVoucherReceivedEvent(@event))
                {
                    writeRecord();
                }
            }
        }

        private bool _handlePowerVoucherReceivedEvent(PowerVoucherReceivedEvent @event)
        {
            bool update = false;

            return update;
        }

        public IDictionary<string, object> GetVariables()
        {
            IDictionary<string, object> variables = new Dictionary<string, object>
            {
                ["criminalrecord"] = new List<FactionRecord>(criminalrecord),
                ["claims"] = claims,
                ["fines"] = fines,
                ["bounties"] = bounties
            };
            return variables;
        }

        public void writeRecord()
        {
            lock (recordLock)
            {
                // Write criminal configuration with current criminal record
                claims = criminalrecord.Sum(r => r.claims);
                fines = criminalrecord.Sum(r => r.fines);
                bounties = criminalrecord.Sum(r => r.bounties);
                CrimeMonitorConfiguration configuration = new CrimeMonitorConfiguration()
                {
                    criminalrecord = criminalrecord,
                    claims = claims,
                    fines = fines,
                    bounties = bounties,
                    updatedat = updateDat
                };
                configuration.ToFile();
            }
            // Make sure the UI is up to date
            RaiseOnUIThread(RecordUpdatedEvent, criminalrecord);
        }

        private void readRecord(CrimeMonitorConfiguration configuration = null)
        {
            lock (recordLock)
            {
                // Obtain current criminal record from configuration
                configuration = configuration ?? CrimeMonitorConfiguration.FromFile();
                claims = configuration.claims;
                fines = configuration.fines;
                bounties = configuration.bounties;
                updateDat = configuration.updatedat;

                // Build a new criminal record
                List<FactionRecord> records = configuration.criminalrecord.OrderBy(c => c.name).ToList();
                criminalrecord.Clear();
                foreach (FactionRecord record in records)
                {
                    criminalrecord.Add(record);
                }
            }
        }

        private FactionRecord AddRecord(string faction)
        {
            if (faction == null) { return null; }

            FactionRecord record = new FactionRecord(faction);
            GetFactionData(record);

            lock (recordLock)
            {
                criminalrecord.Add(record);
            }
            return record;
        }

        private void RemoveRecord(FactionRecord record)
        {
            // Check if crimes are pending
            if (record.reportData == null || !record.reportData.Any())
            {
                if (record.claims + record.fines + record.bounties == 0)
                {
                    _RemoveRecord(record);
                }
            }
        }

        public void _RemoveRecord(FactionRecord record)
        {
            string faction = record.name.ToLowerInvariant();
            lock (recordLock)
            {
                for (int i = 0; i < criminalrecord.Count; i++)
                {
                    if (criminalrecord[i].name.ToLowerInvariant() == faction)
                    {
                        criminalrecord.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        public FactionRecord GetRecordWithFaction(string faction)
        {
            if (faction == null)
            {
                return null;
            }
            return criminalrecord.FirstOrDefault(c => c.name.ToLowerInvariant() == faction.ToLowerInvariant());
        }

        private void AddCrimeReport(string faction, CrimeReport report)
        {
            if (faction != null || report != null)
            {
                FactionRecord record = GetRecordWithFaction(faction);
                if (record == null)
                {
                    record = AddRecord(faction);
                }
                record.reportData.Add(report);
                if (report.bounty)
                {
                    record.bounties = (long)record.reportData.Where(i => i.bounty).Sum(i => i.amount);
                }
                else
                {
                    record.fines = (long)record.reportData.Where(i => !i.bounty).Sum(i => i.amount);
                }
            }
        }

        private bool RemoveCrimeReport(string faction, int shipId, bool bounty)
        {
            if (faction == null) { return false; }

            FactionRecord record = GetRecordWithFaction(faction);
            if (record != null)
            {
                List<CrimeReport> reports = record.reportData.Where(r => r.shipId == shipId && r.bounty == bounty).ToList();
                if (reports != null)
                {
                    long total = (long)reports.Sum(r => r.amount);
                    if (bounty) { record.bounties -= total; } else { record.fines -= total; }
                    record.reportData = record.reportData.Except(reports).ToList();
                    RemoveRecord(record);
                    return true;
                }
            }
            return false;
        }

        public StarSystem GetFactionSystem(string faction, int sphereLy = 20)
        {
            if (faction == null) { return null; }

            string currentSystem = EDDI.Instance?.CurrentStarSystem?.name;
            List<Dictionary<string, object>> sphereSystems = StarMapService.GetStarMapSystemsSphere(currentSystem, 0, sphereLy);

            SortedList<decimal, string> nearestList = new SortedList<decimal, string>();
            foreach (Dictionary<string, object> dict in sphereSystems.ToList())
            {
                decimal? dist = dict["distance"] as decimal?;
                StarSystem system = dict["system"] as StarSystem;
                if (dist != null && system.Faction.name == faction)
                {
                    nearestList.Add(dist ?? 0, system.name);
                }
            }
            string nearestSystem = nearestList.Values.FirstOrDefault();
            StarSystem factionSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(nearestSystem, true);
            return factionSystem;
        }

        public void GetFactionData(FactionRecord record, string system = null, int cubeLy = 20)
        {
            if (record == null || record.name == null || record.name == Properties.CrimeMonitor.blank_faction) { return; }
            string factionName = record.name;

            StarSystem currentSystem = EDDI.Instance?.CurrentStarSystem;
            if (currentSystem == null) { return; }

            // Check for valid 'search' system
            StarSystem searchSystem = currentSystem;
            if (system != null)
            {
                searchSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(system, true);
                if (searchSystem == null) { return; }
            }

            // Get the nearest faction system and station
            List<StarSystem> cubeSystems = StarMapService.GetStarMapSystemsCube(searchSystem.name, cubeLy, false, false, false, false);
            if (cubeSystems != null && cubeSystems.Any())
            {
                List<Station> factionStations = new List<Station>();
                SortedList<decimal, string> nearestList = new SortedList<decimal, string>();

                // Get systems data from local database and fetch any missing systems from EDSM
                // Do not refresh local database data to minimize EDSM queries
                cubeSystems = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystems(cubeSystems.Select(s => s.name).ToArray(), true, false);

                // Filter the systems in which the faction resides
                List<StarSystem> factionSystems = cubeSystems.Where(s => s.factions?.Any(f => f.name == factionName) ?? false).ToList();
                record.knownsystems = factionSystems.Select(s => s.name).ToList();

                // Find the nearest faction system which meets game version and landing pad size requirements
                string shipSize = EDDI.Instance?.CurrentShip?.size ?? "Large";
                foreach (StarSystem starsystem in factionSystems)
                {
                    // Filter stations which meet the game version and landing pad size requirements
                    factionStations = EDDI.Instance.inHorizons ? starsystem.stations : starsystem.orbitalstations
                        .Where(s => s.LandingPadCheck(shipSize)).ToList();

                    // Build list to find the faction system nearest to the current system
                    if (factionStations.Count > 0)
                    {
                        decimal distance = CalculateDistance(currentSystem, starsystem);
                        if (!nearestList.ContainsKey(distance))
                        {
                            nearestList.Add(distance, starsystem.name);
                        }
                    }
                }

                // Nearest faction system
                string nearestSystem = nearestList.Values.FirstOrDefault();
                if (nearestSystem == null) { return; }
                record.system = nearestSystem;
                StarSystem factionSystem = factionSystems.FirstOrDefault(s => s.name == nearestSystem);
                record.Faction = factionSystem.factions.FirstOrDefault(f => f.name == record.name);

                // Filter stations within the faction system which meet the game version and landing pad size requirements
                factionStations = EDDI.Instance.inHorizons ? factionSystem.stations : factionSystem.orbitalstations
                    .Where(s => s.LandingPadCheck(shipSize)).ToList();

                // Build list to find the faction station nearest to the main star
                nearestList.Clear();
                foreach (Station station in factionStations)
                {
                    if (!nearestList.ContainsKey(station.distancefromstar ?? 0))
                    {
                        nearestList.Add(station.distancefromstar ?? 0, station.name);
                    }
                }

                // Faction station nearest to the main star
                string nearestStation = nearestList.Values.FirstOrDefault();
                if (nearestStation == null) { return; }
                record.station = nearestStation;
            }
        }

        public Station GetInterstellarFactorsStation(int cubeLy = 20)
        {
            StarSystem currentSystem = EDDI.Instance?.CurrentStarSystem;
            if (currentSystem == null) { return null; }

            // Get the nearest Interstellar Factors system and station
            List<StarSystem> cubeSystems = StarMapService.GetStarMapSystemsCube(currentSystem.name, cubeLy);
            if (cubeSystems != null && cubeSystems.Any())
            {
                List<Station> IFStations = new List<Station>();
                SortedList<decimal, string> nearestList = new SortedList<decimal, string>();

                string shipSize = EDDI.Instance?.CurrentShip?.size ?? "Large";
                SecurityLevel securityLevel = SecurityLevel.FromName("Low");
                StationService service = StationService.FromEDName("InterstellarFactorsContact");

                // Find the low security level systems which may contain IF contacts
                List<string> systemNames = cubeSystems.Where(s => s.securityLevel == securityLevel).Select(s => s.name).ToList();
                List<StarSystem> IFSystems = DataProviderService.GetSystemsData(systemNames.ToArray(), true, true, true, true, true);

                foreach (StarSystem starsystem in IFSystems)
                {
                    // Filter stations which meet the game version and landing pad size requirements
                    IFStations = EDDI.Instance.inHorizons ? starsystem.stations : starsystem.orbitalstations
                        .Where(s => s.stationServices.Contains(service) && s.LandingPadCheck(shipSize)).ToList();

                    // Build list to find the IF system nearest to the current system
                    if (IFStations.Count > 0)
                    {
                        nearestList.Add(CalculateDistance(currentSystem, starsystem), starsystem.name);
                    }
                }

                // Nearest Interstellar Factors system
                string nearestSystem = nearestList.Values.FirstOrDefault();
                if (nearestSystem == null) { return null; }
                StarSystem IFSystem = IFSystems.FirstOrDefault(s => s.name == nearestSystem);

                // Filter stations within the IF system which meet the game version and landing pad size requirements
                IFStations = EDDI.Instance.inHorizons ? IFSystem.stations : IFSystem.orbitalstations
                    .Where(s => s.stationServices.Contains(service) && s.LandingPadCheck(shipSize)).ToList();

                // Build list to find the IF station nearest to the main star
                nearestList.Clear();
                foreach (Station station in IFStations)
                {
                    nearestList.Add(station.distancefromstar ?? 0, station.name);
                }

                // Interstellar Factors station nearest to the main star
                string nearestStation = nearestList.Values.FirstOrDefault();
                return IFSystem.stations.FirstOrDefault(s => s.name == nearestStation);
            }
            return null;
        }

        private decimal CalculateDistance(StarSystem curr, StarSystem dest)
        {
            return (decimal)Math.Round(Math.Sqrt(Math.Pow((double)(curr.x - dest.x), 2)
                + Math.Pow((double)(curr.y - dest.y), 2)
                + Math.Pow((double)(curr.z - dest.z), 2)), 2);
        }

        static void RaiseOnUIThread(EventHandler handler, object sender)
        {
            if (handler != null)
            {
                SynchronizationContext uiSyncContext = SynchronizationContext.Current ?? new SynchronizationContext();
                if (uiSyncContext == null)
                {
                    handler(sender, EventArgs.Empty);
                }
                else
                {
                    uiSyncContext.Send(delegate { handler(sender, EventArgs.Empty); }, null);
                }
            }
        }
    }
}