using System.Linq;
using System.Text.Json;
using DataModel;
using MinCostMaxFlow;

namespace Allocation
{
    public class TaskAllocator
    {
        private readonly List<DataModel.Task> tasks;
        private readonly List<Node> nodes;
        private readonly Graph graph;
        private readonly int source,
            sink;
        private readonly int[,] costMatrix;

        public TaskAllocator(List<DataModel.Task> tasks, List<Node> nodes, int[,] costMatrix)
        {
            this.tasks = tasks;
            this.nodes = nodes;
            this.costMatrix = costMatrix;

            int n = 2 + tasks.Count + nodes.Count; // Source + Sink + tasks + nodes
            graph = new Graph(n);

            source = 0;
            sink = n - 1;

            BuildGraph();
        }

        private void BuildGraph()
        {
            // Source -> Tasks
            for (int i = 0; i < tasks.Count; i++)
            {
                int taskVertex = 1 + i;
                graph.AddEdge(source, taskVertex, 1, 0); // each task can be selected exactly once
            }

            // Tasks -> Nodes (only if node can run the task alone AND cost != INF)
            for (int i = 0; i < tasks.Count; i++)
            {
                int taskVertex = 1 + i;
                for (int j = 0; j < nodes.Count; j++)
                {
                    int nodeVertex = 1 + tasks.Count + j;

                    int cost = costMatrix[i, j];
                    if (cost == int.MaxValue) // treat as ∞
                        continue;

                    // QUICK CHECK: node must be able to run this task alone
                    if (
                        tasks[i].CpuRequired > nodes[j].CpuCapacity
                        || tasks[i].RamRequired > nodes[j].RamCapacity
                    )
                        continue;

                    graph.AddEdge(taskVertex, nodeVertex, 1, cost);
                }
            }

            // Nodes -> Sink: compute conservative slot bound based on minimal per-task demands
            for (int j = 0; j < nodes.Count; j++)
            {
                int nodeVertex = 1 + tasks.Count + j;

                var assignable = tasks
                    .Select((t, idx) => (task: t, idx))
                    .Where(x =>
                        costMatrix[x.idx, j] != int.MaxValue
                        && x.task.CpuRequired <= nodes[j].CpuCapacity
                        && x.task.RamRequired <= nodes[j].RamCapacity
                    )
                    .ToList();

                if (assignable.Count == 0)
                    continue;

                int minCpu = assignable.Min(x => x.task.CpuRequired);
                int minRam = assignable.Min(x => x.task.RamRequired);

                int cpuBound = nodes[j].CpuCapacity / Math.Max(1, minCpu);
                int ramBound = nodes[j].RamCapacity / Math.Max(1, minRam);

                int resourceBasedSlots = Math.Min(cpuBound, ramBound);

                int finalSlots = Math.Min(nodes[j].Slots, Math.Max(0, resourceBasedSlots));

                if (finalSlots <= 0)
                    continue;

                graph.AddEdge(nodeVertex, sink, finalSlots, 0);
            }
        }

        /// <summary>
        /// Solve using the MCMF solver and extract allocation (task -> node).
        /// We reset flows before solving so Solve() is safe to call multiple times.
        /// </summary>
        public (int maxFlow, int minCost, List<(DataModel.Task, Node)> allocation) Solve()
        {
            // NEW: ensure we start from zero flows (idempotent)
            graph.ResetFlows();

            var mcmf = new MinCostMaxFlow.MinCostMaxFlow(graph, source, sink);
            var (flow, cost) = mcmf.GetMinCostMaxFlow();

            var allocation = new List<(DataModel.Task, Node)>();

            // Extract which task -> node edges have positive flow
            for (int i = 0; i < tasks.Count; i++)
            {
                int taskVertex = 1 + i;

                foreach (var e in graph.Adj[taskVertex])
                {
                    if (e.To != source && e.Flow > 0)
                    {
                        int nodeIndex = e.To - (1 + tasks.Count);
                        if (nodeIndex >= 0 && nodeIndex < nodes.Count)
                        {
                            allocation.Add((tasks[i], nodes[nodeIndex]));
                        }
                    }
                }
            }

            return (flow, cost, allocation);
        }

        public string GetPhase1OutputJson()
        {
            var (flow, cost, allocation) = Solve();

            var assignments = new Dictionary<string, string>();
            foreach (var (t, n) in allocation)
            {
                assignments[t.Id] = n.Id;
            }

            var outputObj = new
            {
                assignments = assignments,
                total_cost = cost,
                assigned_count = assignments.Count,
            };

            return JsonSerializer.Serialize(
                outputObj,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
    }
}
