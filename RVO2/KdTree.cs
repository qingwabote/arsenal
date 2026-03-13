using System;
using System.Collections.Generic;
using Bastard;
using Unity.Collections;
using Unity.Mathematics;

namespace Arsenal.RVO
{
    public struct KdTree
    {
        /**
         * <summary>Defines a node of an agent k-D tree.</summary>
         */
        private struct AgentTreeNode
        {
            internal int begin_;
            internal int end_;
            internal int left_;
            internal int right_;
            internal float maxX_;
            internal float maxY_;
            internal float minX_;
            internal float minY_;
        }

        /**
         * <summary>The maximum size of an agent k-D tree leaf.</summary>
         */
        private const int MAX_LEAF_SIZE = 10;

        private NativeArray<int> agents_;
        private NativeArray<AgentTreeNode> agentTree_;

        /**
         * <summary>Builds an agent k-D tree.</summary>
         */
        public KdTree(ReadOnlySpan<Agent> agents)
        {
            agents_ = new(agents.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < agents_.Length; ++i)
            {
                agents_[i] = i;
            }

            agentTree_ = new(2 * agents_.Length, Allocator.Temp);

            if (agents_.Length != 0)
            {
                buildAgentTreeRecursive(agents, 0, agents_.Length, 0);
            }
        }

        /**
         * <summary>Recursive method for building an agent k-D tree.</summary>
         *
         * <param name="begin">The beginning agent k-D tree node node index.
         * </param>
         * <param name="end">The ending agent k-D tree node index.</param>
         * <param name="nodeIdx">The current agent k-D tree node index.</param>
         */
        private void buildAgentTreeRecursive(ReadOnlySpan<Agent> agents, int begin, int end, int nodeIdx)
        {
            ref readonly var agent = ref agents[agents_[begin]];
            ref var node = ref agentTree_.ElementAt(nodeIdx);
            node.begin_ = begin;
            node.end_ = end;
            node.minX_ = node.maxX_ = agent.position_.x;
            node.minY_ = node.maxY_ = agent.position_.y;

            for (int i = begin + 1; i < end; ++i)
            {
                agent = ref agents[agents_[i]];

                node.maxX_ = math.max(node.maxX_, agent.position_.x);
                node.minX_ = math.min(node.minX_, agent.position_.x);
                node.maxY_ = math.max(node.maxY_, agent.position_.y);
                node.minY_ = math.min(node.minY_, agent.position_.y);
            }

            if (end - begin > MAX_LEAF_SIZE)
            {
                /* No leaf node. */
                bool isVertical = node.maxX_ - node.minX_ > node.maxY_ - node.minY_;
                float splitValue = 0.5f * (isVertical ? node.maxX_ + node.minX_ : node.maxY_ + node.minY_);

                int left = begin;
                int right = end;

                while (left < right)
                {
                    agent = ref agents[agents_[left]];
                    while (left < right && (isVertical ? agent.position_.x : agent.position_.y) < splitValue)
                    {
                        agent = ref agents[agents_[++left]];
                    }

                    agent = ref agents[agents_[right - 1]];
                    while (right > left && (isVertical ? agent.position_.x : agent.position_.y) >= splitValue)
                    {
                        agent = ref agents[agents_[--right - 1]];
                    }

                    if (left < right)
                    {
                        (agents_[left], agents_[right - 1]) = (agents_[right - 1], agents_[left]);
                        ++left;
                        --right;
                    }
                }

                int leftSize = left - begin;

                if (leftSize == 0)
                {
                    ++leftSize;
                    ++left;
                    ++right;
                }

                node.left_ = nodeIdx + 1;
                node.right_ = nodeIdx + 2 * leftSize;

                buildAgentTreeRecursive(agents, begin, left, node.left_);
                buildAgentTreeRecursive(agents, left, end, node.right_);
            }
        }

        /**
         * <summary>Recursive method for computing the agent neighbors of the
         * specified agent.</summary>
         *
         * <param name="agentID">The agent for which agent neighbors are to be
         * computed.</param>
         * <param name="rangeSq">The squared range around the agent.</param>
         * <param name="node">The current agent k-D tree node index.</param>
         */
        public void queryAgentTreeRecursive(ReadOnlySpan<Agent> agents, int agentID, Span<KeyValuePair<float, int>> neighbors, ref int count, ref float rangeSq, int nodeIdx)
        {
            ref readonly var agent = ref agents[agentID];
            ref var node = ref agentTree_.ElementAt(nodeIdx);
            if (node.end_ - node.begin_ <= MAX_LEAF_SIZE)
            {
                for (int nodeIndex = node.begin_; nodeIndex < node.end_; ++nodeIndex)
                {
                    if (agentID != agents_[nodeIndex])
                    {
                        float distSq = math.lengthsq(agent.position_ - agents[agents_[nodeIndex]].position_);

                        if (distSq < rangeSq)
                        {
                            if (count < agent.maxNeighbors_)
                            {
                                neighbors[count++] = new KeyValuePair<float, int>(distSq, agents_[nodeIndex]);
                            }

                            int i = count - 1;

                            while (i != 0 && distSq < neighbors[i - 1].Key)
                            {
                                neighbors[i] = neighbors[i - 1];
                                --i;
                            }

                            neighbors[i] = new KeyValuePair<float, int>(distSq, agents_[nodeIndex]);

                            if (count == agent.maxNeighbors_)
                            {
                                rangeSq = neighbors[count - 1].Key;
                            }
                        }
                    }
                }
            }
            else
            {
                var position = agent.position_;
                ref var left = ref agentTree_.ElementAt(node.left_);
                ref var right = ref agentTree_.ElementAt(node.right_);
                float distSqLeft = math.square(math.max(0.0f, left.minX_ - position.x)) + math.square(math.max(0.0f, position.x - left.maxX_)) + math.square(math.max(0.0f, left.minY_ - position.y)) + math.square(math.max(0.0f, position.y - left.maxY_));
                float distSqRight = math.square(math.max(0.0f, right.minX_ - position.x)) + math.square(math.max(0.0f, position.x - right.maxX_)) + math.square(math.max(0.0f, right.minY_ - position.y)) + math.square(math.max(0.0f, position.y - right.maxY_));

                if (distSqLeft < distSqRight)
                {
                    if (distSqLeft < rangeSq)
                    {
                        queryAgentTreeRecursive(agents, agentID, neighbors, ref count, ref rangeSq, node.left_);

                        if (distSqRight < rangeSq)
                        {
                            queryAgentTreeRecursive(agents, agentID, neighbors, ref count, ref rangeSq, node.right_);
                        }
                    }
                }
                else
                {
                    if (distSqRight < rangeSq)
                    {
                        queryAgentTreeRecursive(agents, agentID, neighbors, ref count, ref rangeSq, node.right_);

                        if (distSqLeft < rangeSq)
                        {
                            queryAgentTreeRecursive(agents, agentID, neighbors, ref count, ref rangeSq, node.left_);
                        }
                    }
                }

            }
        }
    }
}
