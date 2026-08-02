using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Medieval.NpcMovement;

namespace Medieval.Npcs
{
    /// <summary>
    /// Keeps pooled <see cref="NpcWorldHealthBar"/> instances synced to living DOTS NPCs with
    /// <see cref="NpcCharacterCombatState"/>. Bars appear only while damaged (same as managed CharacterHealthBar).
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NpcHealthBarPresentationSystem : SystemBase
    {
        readonly Dictionary<Entity, NpcWorldHealthBar> _active = new Dictionary<Entity, NpcWorldHealthBar>(128);
        readonly List<NpcWorldHealthBar> _pool = new List<NpcWorldHealthBar>(32);
        readonly List<Entity> _toRemove = new List<Entity>(32);
        readonly HashSet<Entity> _seen = new HashSet<Entity>();
        Transform _root;

        protected override void OnDestroy()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null)
                    Object.Destroy(_pool[i].gameObject);
            }

            _pool.Clear();

            foreach (var kv in _active)
            {
                if (kv.Value != null)
                    Object.Destroy(kv.Value.gameObject);
            }

            _active.Clear();

            if (_root != null)
                Object.Destroy(_root.gameObject);
            _root = null;
        }

        protected override void OnUpdate()
        {
            EnsureRoot();
            _seen.Clear();

            foreach (var (combat, xp, ltw, entity) in SystemAPI
                         .Query<RefRO<NpcCharacterCombatState>, RefRO<NpcExperience>, RefRO<LocalToWorld>>()
                         .WithNone<NpcDeadTag>()
                         .WithEntityAccess())
            {
                var state = combat.ValueRO;
                if (state.IsDead != 0)
                    continue;

                bool damaged = state.CurrentHealth > 0.001f &&
                               state.CurrentHealth < state.MaxHealth - 0.001f;
                if (!damaged)
                {
                    if (_active.TryGetValue(entity, out NpcWorldHealthBar idleBar))
                        Release(entity, idleBar);
                    continue;
                }

                _seen.Add(entity);
                var p = ltw.ValueRO.Position;
                var feet = new Vector3(p.x, p.y, p.z);

                if (!_active.TryGetValue(entity, out NpcWorldHealthBar bar))
                {
                    bar = Rent();
                    _active[entity] = bar;
                }

                bar.Sync(feet, state.CurrentHealth, state.MaxHealth, xp.ValueRO.Level);
            }

            _toRemove.Clear();
            foreach (var kv in _active)
            {
                if (!_seen.Contains(kv.Key))
                    _toRemove.Add(kv.Key);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                Entity e = _toRemove[i];
                if (_active.TryGetValue(e, out NpcWorldHealthBar bar))
                    Release(e, bar);
            }
        }

        void EnsureRoot()
        {
            if (_root != null)
                return;
            var go = new GameObject("NpcWorldHealthBars");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        NpcWorldHealthBar Rent()
        {
            NpcWorldHealthBar bar;
            if (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                bar = _pool[last];
                _pool.RemoveAt(last);
            }
            else
            {
                bar = NpcWorldHealthBar.Create(_root);
            }

            if (bar != null)
                bar.SetVisible(true);
            return bar;
        }

        void Release(Entity entity, NpcWorldHealthBar bar)
        {
            _active.Remove(entity);
            if (bar == null)
                return;
            bar.SetVisible(false);
            _pool.Add(bar);
        }
    }
}
