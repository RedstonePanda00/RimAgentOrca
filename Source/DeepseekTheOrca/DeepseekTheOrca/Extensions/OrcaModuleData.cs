using System;
using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    // Main-thread, owner-namespaced storage. Values use normal RimWorld Scribe,
    // including cross references; runtime tasks must not be serialized.
    public sealed class OrcaModuleData : IExposable
    {
        private List<OrcaModuleDataEntry> entries = new List<OrcaModuleDataEntry>();
        private Dictionary<string, OrcaModuleDataEntry> index;

        public T Get<T>(string ownerId) where T : class, IExposable, new()
        {
            return (T)Get(ownerId, typeof(T));
        }

        public IExposable Get(string ownerId, Type type)
        {
            Validate(ownerId, type);
            EnsureIndex();
            OrcaModuleDataEntry entry;
            if (index.TryGetValue(ownerId, out entry))
            {
                if (entry.value != null && !type.IsInstanceOfType(entry.value))
                    throw new InvalidOperationException("Module data type changed for " + ownerId);
                if (entry.value == null) entry.value = (IExposable)Activator.CreateInstance(type);
                return entry.value;
            }
            var value = (IExposable)Activator.CreateInstance(type);
            Set(ownerId, value);
            return value;
        }

        public void Set(string ownerId, IExposable value)
        {
            if (value == null) throw new ArgumentNullException("value");
            Validate(ownerId, value.GetType());
            EnsureIndex();
            OrcaModuleDataEntry entry;
            if (index.TryGetValue(ownerId, out entry))
            {
                if (entry.value != null && entry.value.GetType() != value.GetType())
                    throw new InvalidOperationException("Module data type changed for " + ownerId);
                entry.value = value;
            }
            else
            {
                entry = new OrcaModuleDataEntry { owner = ownerId, value = value };
                entries.Add(entry);
                index.Add(ownerId, entry);
            }
        }

        private static void Validate(string ownerId, Type type)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("A stable module owner ID is required.");
            if (type == null || !typeof(IExposable).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException("Module data must implement IExposable and have a public parameterless constructor.");
        }

        private void EnsureIndex()
        {
            if (index != null) return;
            index = new Dictionary<string, OrcaModuleDataEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.owner)) index[entry.owner] = entry;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (entries == null) entries = new List<OrcaModuleDataEntry>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) index = null;
        }
    }

    public sealed class OrcaModuleDataEntry : IExposable
    {
        public string owner;
        public IExposable value;
        public void ExposeData()
        {
            Scribe_Values.Look(ref owner, "owner");
            Scribe_Deep.Look(ref value, "value");
        }
    }

    public static class OrcaModuleStorage
    {
        public static OrcaModuleData ForGame(Game game)
        {
            if (game == null) throw new InvalidOperationException("Module game state needs an active game.");
            return game.GetComponent<DeepseekTheOrcaGameComponent>().moduleData;
        }

        public static T GameState<T>(string ownerId) where T : class, IExposable, new()
        { return ForGame(Current.Game).Get<T>(ownerId); }

        public static T Settings<T>(string ownerId) where T : class, IExposable, new()
        {
            if (DeepseekTheOrcaMod.Settings == null) throw new InvalidOperationException("Mod settings are not loaded.");
            return DeepseekTheOrcaMod.Settings.moduleData.Get<T>(ownerId);
        }
    }
}
