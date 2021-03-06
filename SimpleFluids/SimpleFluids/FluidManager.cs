﻿using System;
using System.Collections.Generic;
using System.Threading;
using BlockTypes;
using Pipliz;

namespace SimpleFluids
{
    [ModLoader.ModManager]
    public static class FluidManager
    {
        static Thread _FluidsThread = new Thread(FluidActions);
        public static Pipliz.Collections.BinaryHeap<long, Action> _actions = new Pipliz.Collections.BinaryHeap<long, Action>(8);
        public static AutoResetEvent _SomeAction = new AutoResetEvent(false);

        public static FluidInfo[] _fluids = new FluidInfo[(int)EFluids.MAX];

        public static string MODPATH;

        public const int L_MAX_DISTANCE = 7;
        public const int L_MIN_DISTANCE = 2;
        public const int L_DEFAULT_DISTANCE = 3;

        public const long L_MAX_SPEED = 500;
        public const long L_MIN_SPEED = 200;
        public const long L_DEFAULT_SPEED = 300;
        
        /*

        [ModLoader.ModCallback(ModLoader.EModCallbackType.AfterWorldLoad, "Khanx.SimpleFluids.GetModPath")]
        public static void LoadConfig()
        {
            FluidInfo Water = new FluidInfo();
            FluidInfo Lava = new FluidInfo();

            Water.source = ItemTypes.IndexLookup.GetIndex("Khanx.SimpleFluids.Water");
            Water.fake = ItemTypes.IndexLookup.GetIndex("Khanx.SimpleFluids.Fake.Water");
            Water.bucket = ItemTypes.IndexLookup.GetIndex("Khanx.SimpleFluids.WaterBucket");

            Lava.source = ItemTypes.IndexLookup.GetIndex("Khanx.SimpleFluids.Lava");
            Lava.fake = ItemTypes.IndexLookup.GetIndex("Khanx.SimpleFluids.Fake.Lava");
            Lava.bucket = ItemTypes.IndexLookup.GetIndex("Khanx.SimpleFluids.LavaBucket");

            try
            {
                JSONNode config = JSON.Deserialize(MODPATH + "/config.json");

                config.TryGetChild("water", out JSONNode waterConfig);

                if(!waterConfig.TryGetAs<int>("spreadDistance", out int w_spreadDistance))
                    Water.distance = W_DEFAULT_DISTANCE;
                else if(w_spreadDistance > W_MAX_DISTANCE || w_spreadDistance < W_MIN_DISTANCE)
                {
                    Log.Write(string.Format("<color=red>Warning: Water spreadDistance must be between {0} and {1} included</color>", W_MIN_DISTANCE, W_MAX_DISTANCE));
                    Water.distance = W_DEFAULT_DISTANCE;
                }
                else
                    Water.distance = w_spreadDistance;

                if(!waterConfig.TryGetAs<long>("spreadSpeed", out long w_spreadSpeed))
                    Water.time = W_DEFAULT_SPEED;
                else if(w_spreadSpeed > W_MAX_SPEED || w_spreadSpeed < W_MIN_SPEED)
                {
                    Log.Write(string.Format("<color=red>Warning: Water spreadSpeed must be between {0} and {1} included</color>", W_MIN_SPEED, W_MAX_SPEED));
                    Water.time = W_DEFAULT_SPEED;
                }
                else
                    Water.time = w_spreadSpeed;

                config.TryGetChild("lava", out JSONNode lavaConfig);

                if(!lavaConfig.TryGetAs<int>("spreadDistance", out int l_spreadDistance))
                    Lava.distance = L_DEFAULT_DISTANCE;
                else if(l_spreadDistance > L_MAX_DISTANCE || l_spreadDistance < L_MIN_DISTANCE)
                {
                    Log.Write(string.Format("<color=red>Warning: Lava spreadDistance must be between {0} and {1} included</color>", L_MIN_DISTANCE, L_MAX_DISTANCE));
                    Lava.distance = L_DEFAULT_DISTANCE;
                }
                else
                    Lava.distance = l_spreadDistance;

                if(!lavaConfig.TryGetAs<long>("spreadSpeed", out long l_spreadSpeed))
                    Lava.time = L_DEFAULT_SPEED;
                else if(l_spreadSpeed > L_MAX_SPEED || l_spreadSpeed < L_MIN_SPEED)
                {
                    Log.Write(string.Format("<color=red>Warning: Lava spreadSpeed must be between {0} and {1} included</color>", L_MIN_SPEED, L_MAX_SPEED));
                    Lava.time = L_DEFAULT_SPEED;
                }
                else
                    Lava.time = l_spreadSpeed;
            }
            catch(Exception)
            {
                Water.distance = W_DEFAULT_DISTANCE;
                Water.time = W_DEFAULT_SPEED;

                Lava.distance = L_DEFAULT_DISTANCE;
                Lava.time = L_DEFAULT_SPEED;
            }


            _fluids[(int)EFluids.Water] = Water;
            _fluids[(int)EFluids.Lava] = Lava;
            Buckets.EmptyBucket.fluidsInfo.Add(Water.source, EFluids.Water);
            Buckets.EmptyBucket.fluidsInfo.Add(BuiltinBlocks.Indices.water, EFluids.Water);
            Buckets.EmptyBucket.fluidsInfo.Add(Lava.source, EFluids.Lava);
        }
        */
        

        static FluidManager()
        {
            _FluidsThread.IsBackground = true;
            _FluidsThread.Start();
        }

        public static void FluidActions()
        {
            while(true)
            {
                try
                {
                    while(_actions.Count == 0)
                        _SomeAction.WaitOne();

                    long timeToSleep = _actions.PeekMinKey() - Time.MillisecondsSinceStart;

                    if(timeToSleep > 0)
                    {
                        Thread.Sleep((int)timeToSleep);
                    }

                    _actions.ExtractValueMin()();
                }
                catch(Exception e)
                {
                    Log.Write(e.Message);
                }
            }
        }

        private static Vector3Int[] adjacents = { Vector3Int.left, Vector3Int.forward, Vector3Int.right, Vector3Int.back };

        public static void Spread(Vector3Int position, EFluids fluid, int distance = int.MinValue, bool start = true)
        {
            //Log.Write(string.Format("<color=blue>Spread {0}</color>", position));

            FluidInfo info = _fluids[(int)fluid];

            if(distance == int.MinValue)
                distance = info.distance;

            if(distance <= 0)
                return;

            if(start)
            {
                ThreadManager.InvokeOnMainThread(() =>
                {
                    if(World.TryGetTypeAt(position, out ushort posType) && ( posType == BuiltinBlocks.Indices.air || posType == info.fake ))
                        ServerManager.TryChangeBlock(position, info.source);
                });
            }
            else
            {
                ThreadManager.InvokeOnMainThread(() =>
                {
                    if(World.TryGetTypeAt(position, out ushort posType) && ( posType == BuiltinBlocks.Indices.air ))
                        ServerManager.TryChangeBlock(position, info.fake);
                });
            }

            Vector3Int down = position + Vector3Int.down;

            if(!World.TryGetTypeAt(down, out ushort typeDown))
                return;

            //If DOWN is source -> IGNORE
            if(typeDown == info.source)
                return;

            //If down is air or fake.fluid -> SPREAD DOWN
            if(typeDown == BuiltinBlocks.Indices.air || typeDown == info.fake)
            {
                _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                {
                    Spread(down, fluid);
                });

                _SomeAction.Set();
                return;
            }

            foreach(var adjacent in adjacents)
            {
                var adj = position + adjacent;

                if(!World.TryGetTypeAt(adj, out ushort typeAdj))
                    continue;

                if(typeAdj == BuiltinBlocks.Indices.air || typeAdj == info.fake)
                {
                    var adjDown = adj + Vector3Int.down;

                    if(!World.TryGetTypeAt(adjDown, out ushort typeAdjD))
                        continue;

                    if(typeAdjD == info.source) // Source
                        continue;

                    //Continue spreading down
                    if(typeAdjD == BuiltinBlocks.Indices.air)
                    {
                        _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                        {
                            Spread(adjDown, fluid);
                        });

                        _SomeAction.Set();
                    }
                    else //Spread Side
                    {
                        _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                        {
                            Spread(adj, fluid, distance - 1, false);
                        });

                        _SomeAction.Set();
                    }
                }
            }
        }

        public static void Remove(Vector3Int position, EFluids fluid, ushort newType = ushort.MaxValue)
        {
            //Log.Write(string.Format("<color=blue>Remove {0}</color>", position));

            FluidInfo info = _fluids[(int)fluid];

            if(!World.TryGetTypeAt(position, out ushort posToRemove) && ( posToRemove != info.source && posToRemove != info.fake ))
                return;

            ThreadManager.InvokeOnMainThread(() =>
            {
                if(newType == ushort.MaxValue)
                    ServerManager.TryChangeBlock(position, BuiltinBlocks.Indices.air);
                else
                    ServerManager.TryChangeBlock(position, newType);
            });

            var down = position + Vector3Int.down;

            if(!World.TryGetTypeAt(down, out ushort typeDown))
                return;

            //If DOWN is source
            if(typeDown == info.source)
            {
                _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                {
                    Remove(down, fluid);
                });

                _SomeAction.Set();
                return;
            }

            foreach(var adjacent in adjacents)
            {
                var adj = position + adjacent;

                if(!World.TryGetTypeAt(adj, out ushort typeAdj))
                    continue;

                if(typeAdj == info.fake)
                {
                    _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                    {
                        TryRemove(adj, fluid, info.distance - 1);
                    });

                    _SomeAction.Set();
                }
                else if(typeAdj == BuiltinBlocks.Indices.air)
                {
                    var adjD = adj + Vector3Int.down;

                    if(!World.TryGetTypeAt(adjD, out ushort typeAdjD))
                        continue;

                    if(typeAdjD == info.source)
                    {
                        _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                        {
                            Remove(adjD, fluid);
                        });

                        _SomeAction.Set();
                    }
                }
            }
        }

        public static void TryRemove(Vector3Int position, EFluids fluid, int distance = int.MinValue)
        {
            //Log.Write(string.Format("<color=blue>TryRemove {0}</color>", position));

            FluidInfo info = _fluids[(int)fluid];

            if(!World.TryGetTypeAt(position, out ushort typeToRemove) && ( typeToRemove != info.source && typeToRemove != info.fake ))
                return;

            if(distance == int.MinValue)
                distance = info.distance;

            if(distance < 0)
                return;

            if(!World.TryGetTypeAt(position, out ushort type))
                return;

            if(type != info.fake)
                return;

            if(ClosestSource(position, fluid) == Vector3Int.maximum)
            {
                ThreadManager.InvokeOnMainThread(() =>
                {
                    if(World.TryGetTypeAt(position, out ushort posToRemove) && ( posToRemove == info.fake ))
                        ServerManager.TryChangeBlock(position, BuiltinBlocks.Indices.air);
                });
            }

            var down = position + Vector3Int.down;

            if(!World.TryGetTypeAt(down, out ushort typeDown))
                return;

            if(typeDown == info.source)
            {
                _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                {
                    Remove(down, fluid);
                });

                _SomeAction.Set();
                return;
            }

            foreach(var adjacent in adjacents)
            {
                var adj = position + adjacent;

                if(!World.TryGetTypeAt(adj, out ushort typeAdj))
                    continue;

                if(typeAdj == info.fake)
                {
                    _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                    {
                        TryRemove(adj, fluid, distance - 1);
                    });

                    _SomeAction.Set();
                }
                else if(typeAdj == BuiltinBlocks.Indices.air)
                {
                    var adjD = adj + Vector3Int.down;

                    if(!World.TryGetTypeAt(adjD, out ushort typeAdjD))
                        continue;

                    if(typeAdjD == info.source)
                    {
                        _actions.Add(Time.MillisecondsSinceStart + info.time, delegate ()
                        {
                            Remove(adjD, fluid);
                        });

                        _SomeAction.Set();
                    }
                }
            }
        }

        public static Vector3Int ClosestSource(Vector3Int position, EFluids fluid)
        {
            FluidInfo info = _fluids[(int)fluid];

            LinkedList<(Vector3Int, int)> toVisit = new LinkedList<(Vector3Int, int)>();

            toVisit.AddLast((position, 0));

            List<Vector3Int> alreadyVisited = new List<Vector3Int>();

            while(toVisit.Count > 0)
            {
                var node = toVisit.First.Value;
                toVisit.RemoveFirst();

                if(alreadyVisited.Contains(node.Item1))
                    continue;

                alreadyVisited.Add(node.Item1);

                if(node.Item2 == info.distance)
                    continue;

                foreach(var adjacent in adjacents)
                {
                    var adj = node.Item1 + adjacent;

                    if(!World.TryGetTypeAt(adj, out ushort typeadj))
                        continue;

                    if(typeadj == info.source)
                        return adj;

                    if(typeadj == info.fake)
                        if(!alreadyVisited.Contains(adj))
                            toVisit.AddLast((adj, node.Item2 + 1));
                }

            }

            return Vector3Int.maximum;
        }
    }
}
