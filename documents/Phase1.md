# Phase-1 — README (Task Allocation with Min-Cost Max-Flow)
📌 Overview

Phase 1 focuses on assigning tasks to computational nodes.
The goal is to:

Maximize the number of assigned tasks (Max Flow)

Minimize the total execution cost (Min Cost)

This is solved using the Min-Cost Max-Flow (MCMF) algorithm, implemented with the Successive Shortest Path method.
For shortest paths, we use Bellman–Ford, which correctly handles negative-cost edges (used in residual graphs).

## 🗂️ Data Model
🔹 Task

Represents a computational job that must be scheduled.

Id (string): Unique task identifier, e.g., "T1".

CpuRequired (int): CPU resources required.

RamRequired (int): RAM resources required.

Deadline (int): Deadline (time slot by which the task should finish).

Note: Deadlines are not enforced in Phase 1; they are only used in Phase 2 (scheduling).

🔹 Node

Represents a computational node (server, machine, or VM).

Id (string): Unique node identifier, e.g., "N1".

CpuCapacity (int): Maximum CPU capacity of the node.

RamCapacity (int): Maximum RAM capacity of the node.

Slots (int): Maximum number of tasks this node can host (Phase 1 concurrency).

Think of Slots as how many tasks can run simultaneously on this node.

## 🔹Edge

Represents a directed edge in the flow network.

From / To (int): Vertex indices in the graph.

Capacity (int): Maximum flow allowed.

Cost (int): Cost per unit flow.

Flow (int): Current flow.

Reverse (Edge): Reverse edge (for residual graph).

RemainingCapacity: Capacity - Flow.

AddFlow(f): Updates both forward and reverse edges.

## 🔹Graph

Adjacency-list representation for MCMF.

VertexCount (int): Number of vertices.

Adj (List<Edge>[]): Adjacency list.

AddEdge(from, to, capacity, cost): Adds forward + reverse edges.

⚙️ Algorithm — MinCostMaxFlow

Input: Graph g, source, sink

Steps:

Run Bellman–Ford on residual graph → find shortest path (by cost).

If sink unreachable → stop.

Reconstruct path via parent[].

Compute augFlow = min(RemainingCapacity) on path.

Push flow with AddFlow(augFlow).

Update totals:

flow += augFlow

cost += augFlow * edge.Cost

Output: (maxFlow, minCost)

🛠️ Task Allocator (TaskAllocator)

Responsible for building the flow network and solving MCMF.

Graph Construction

Source index = 0

Tasks = vertices 1 .. T

Nodes = vertices (1+T) .. (T+N)

Sink = last vertex

Edges:

Source → Task_i : capacity = 1, cost = 0

Each task assigned at most once.

Task_i → Node_j : capacity = 1, cost = costMatrix[i,j]

Only if cost ≠ int.MaxValue.

Node_j → Sink : capacity = Slots, cost = 0

Node can run multiple tasks, up to concurrency limit.

Output of Solve()

flow: number of tasks assigned

minCost: total execution cost

allocation: list of (Task, Node) pairs

Also:
GetPhase1OutputJson() → JSON summary, e.g.

{
  "assignments": {
    "T1": "N2",
    "T2": "N1"
  },
  "total_cost": 9
}

▶️ Example Run (Program.cs)

Input:

Tasks:

T1(cpu=2, ram=4)

T2(cpu=1, ram=2)

T3(cpu=3, ram=3)

Nodes:

N1(cpu=5, ram=6, slots=2)

N2(cpu=6, ram=5, slots=2)

Cost Matrix:

T1: N1=4, N2=2
T2: N1=3, N2=4
T3: N1=2, N2=3


Output:

=== Phase 1: Assignment ===
Assigned tasks (flow) = 3, total cost = 9
  T1 -> N2
  T2 -> N1
  T3 -> N1

Phase1 JSON output:
{
  "assignments": {
    "T1": "N2",
    "T2": "N1",
    "T3": "N1"
  },
  "total_cost": 9
}

⏱️ Complexity

Bellman–Ford per iteration = O(VE)

Total = O(F × V × E)

F = max flow (≈ number of tasks)

✅ Works fine for medium input sizes.
⚠️ For large-scale problems → consider SPFA or Dijkstra with potentials.