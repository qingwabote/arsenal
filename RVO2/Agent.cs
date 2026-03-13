using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Arsenal.RVO
{
    public readonly struct Agent
    {
        static public readonly int NeighborsLimit = 3;

        internal static float det(float2 vector1, float2 vector2)
        {
            return vector1.x * vector2.y - vector1.y * vector2.x;
        }

        internal readonly static float RVO_EPSILON = 0.00001f;

        public readonly float2 position_;
        public readonly float2 velocity_;
        public readonly float2 prefVelocity_;
        public readonly float radius_;
        public readonly float maxSpeed_;
        public readonly int maxNeighbors_;
        public readonly float neighborDist_;
        public readonly float timeHorizon_;

        public Agent(float2 position, float2 velocity, float2 prefVelocity, float radius, float maxSpeed, int maxNeighbors, float neighborDist, float timeHorizon)
        {
            position_ = position;
            velocity_ = velocity;
            prefVelocity_ = prefVelocity;
            radius_ = radius;
            maxSpeed_ = maxSpeed;
            maxNeighbors_ = math.min(maxNeighbors, NeighborsLimit);
            neighborDist_ = neighborDist;
            timeHorizon_ = timeHorizon;
        }

        public void computeORCALines(float timeStep, ReadOnlySpan<Agent> agents, ReadOnlySpan<KeyValuePair<float, int>> neighbors, Span<Line> orcaLines)
        {
            float invTimeHorizon = 1.0f / timeHorizon_;

            /* Create agent ORCA lines. */
            for (int i = 0; i < neighbors.Length; ++i)
            {
                ref readonly var other = ref agents[neighbors[i].Value];

                float2 relativePosition = other.position_ - position_;
                float2 relativeVelocity = velocity_ - other.velocity_;
                float distSq = math.lengthsq(relativePosition);
                float combinedRadius = radius_ + other.radius_;
                float combinedRadiusSq = math.square(combinedRadius);

                Line line;
                float2 u;

                if (distSq > combinedRadiusSq)
                {
                    /* No collision. */
                    float2 w = relativeVelocity - invTimeHorizon * relativePosition;

                    /* Vector from cutoff center to relative velocity. */
                    float wLengthSq = math.lengthsq(w);
                    float dotProduct1 = math.dot(w, relativePosition);

                    if (dotProduct1 < 0.0f && math.square(dotProduct1) > combinedRadiusSq * wLengthSq)
                    {
                        /* Project on cut-off circle. */
                        float wLength = math.sqrt(wLengthSq);
                        float2 unitW = w / wLength;

                        line.direction = new float2(unitW.y, -unitW.x);
                        u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                    }
                    else
                    {
                        /* Project on legs. */
                        float leg = math.sqrt(distSq - combinedRadiusSq);

                        if (det(relativePosition, w) > 0.0f)
                        {
                            /* Project on left leg. */
                            line.direction = new float2(relativePosition.x * leg - relativePosition.y * combinedRadius, relativePosition.x * combinedRadius + relativePosition.y * leg) / distSq;
                        }
                        else
                        {
                            /* Project on right leg. */
                            line.direction = -new float2(relativePosition.x * leg + relativePosition.y * combinedRadius, -relativePosition.x * combinedRadius + relativePosition.y * leg) / distSq;
                        }

                        float dotProduct2 = math.dot(relativeVelocity, line.direction);
                        u = dotProduct2 * line.direction - relativeVelocity;
                    }
                }
                else
                {
                    /* Collision. Project on cut-off circle of time timeStep. */
                    float invTimeStep = 1.0f / timeStep;

                    /* Vector from cutoff center to relative velocity. */
                    float2 w = relativeVelocity - invTimeStep * relativePosition;

                    float wLength = math.length(w);
                    float2 unitW = w / wLength;

                    line.direction = new float2(unitW.y, -unitW.x);
                    u = (combinedRadius * invTimeStep - wLength) * unitW;
                }

                line.point = velocity_ + 0.5f * u;
                orcaLines[i] = line;
            }
        }

        /**
         * <summary>Computes the new velocity of this agent.</summary>
         */
        public float2 computeNewVelocity(ReadOnlySpan<Line> orcaLines)
        {
            float2 newVelocity = default;
            int lineFail = linearProgram2(orcaLines, maxSpeed_, prefVelocity_, false, ref newVelocity);
            if (lineFail < orcaLines.Length)
            {
                linearProgram3(orcaLines, 0, lineFail, maxSpeed_, ref newVelocity);
            }
            return newVelocity;
        }

        /**
         * <summary>Solves a one-dimensional linear program on a specified line
         * subject to linear constraints defined by lines and a circular
         * constraint.</summary>
         *
         * <returns>True if successful.</returns>
         *
         * <param name="lines">Lines defining the linear constraints.</param>
         * <param name="lineNo">The specified line constraint.</param>
         * <param name="radius">The radius of the circular constraint.</param>
         * <param name="optVelocity">The optimization velocity.</param>
         * <param name="directionOpt">True if the direction should be optimized.
         * </param>
         * <param name="result">A reference to the result of the linear program.
         * </param>
         */
        private bool linearProgram1(ReadOnlySpan<Line> lines, int lineNo, float radius, float2 optVelocity, bool directionOpt, ref float2 result)
        {
            float dotProduct = math.dot(lines[lineNo].point, lines[lineNo].direction);
            float discriminant = math.square(dotProduct) + math.square(radius) - math.lengthsq(lines[lineNo].point);

            if (discriminant < 0.0f)
            {
                /* Max speed circle fully invalidates line lineNo. */
                return false;
            }

            float sqrtDiscriminant = math.sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < lineNo; ++i)
            {
                float denominator = det(lines[lineNo].direction, lines[i].direction);
                float numerator = det(lines[i].direction, lines[lineNo].point - lines[i].point);

                if (math.abs(denominator) <= RVO_EPSILON)
                {
                    /* Lines lineNo and i are (almost) parallel. */
                    if (numerator < 0.0f)
                    {
                        return false;
                    }

                    continue;
                }

                float t = numerator / denominator;

                if (denominator >= 0.0f)
                {
                    /* Line i bounds line lineNo on the right. */
                    tRight = math.min(tRight, t);
                }
                else
                {
                    /* Line i bounds line lineNo on the left. */
                    tLeft = math.max(tLeft, t);
                }

                if (tLeft > tRight)
                {
                    return false;
                }
            }

            if (directionOpt)
            {
                /* Optimize direction. */
                if (math.dot(optVelocity, lines[lineNo].direction) > 0.0f)
                {
                    /* Take right extreme. */
                    result = lines[lineNo].point + tRight * lines[lineNo].direction;
                }
                else
                {
                    /* Take left extreme. */
                    result = lines[lineNo].point + tLeft * lines[lineNo].direction;
                }
            }
            else
            {
                /* Optimize closest point. */
                float t = math.dot(lines[lineNo].direction, optVelocity - lines[lineNo].point);

                if (t < tLeft)
                {
                    result = lines[lineNo].point + tLeft * lines[lineNo].direction;
                }
                else if (t > tRight)
                {
                    result = lines[lineNo].point + tRight * lines[lineNo].direction;
                }
                else
                {
                    result = lines[lineNo].point + t * lines[lineNo].direction;
                }
            }

            return true;
        }

        /**
         * <summary>Solves a two-dimensional linear program subject to linear
         * constraints defined by lines and a circular constraint.</summary>
         *
         * <returns>The number of the line it fails on, and the number of lines
         * if successful.</returns>
         *
         * <param name="lines">Lines defining the linear constraints.</param>
         * <param name="radius">The radius of the circular constraint.</param>
         * <param name="optVelocity">The optimization velocity.</param>
         * <param name="directionOpt">True if the direction should be optimized.
         * </param>
         * <param name="result">A reference to the result of the linear program.
         * </param>
         */
        private int linearProgram2(ReadOnlySpan<Line> lines, float radius, float2 optVelocity, bool directionOpt, ref float2 result)
        {
            if (directionOpt)
            {
                /*
                 * Optimize direction. Note that the optimization velocity is of
                 * unit length in this case.
                 */
                result = optVelocity * radius;
            }
            else if (math.lengthsq(optVelocity) > math.square(radius))
            {
                /* Optimize closest point and outside circle. */
                result = math.normalize(optVelocity) * radius;
            }
            else
            {
                /* Optimize closest point and inside circle. */
                result = optVelocity;
            }

            for (int i = 0; i < lines.Length; ++i)
            {
                if (det(lines[i].direction, lines[i].point - result) > 0.0f)
                {
                    /* Result does not satisfy constraint i. Compute new optimal result. */
                    float2 tempResult = result;
                    if (!linearProgram1(lines, i, radius, optVelocity, directionOpt, ref result))
                    {
                        result = tempResult;

                        return i;
                    }
                }
            }

            return lines.Length;
        }

        /**
         * <summary>Solves a two-dimensional linear program subject to linear
         * constraints defined by lines and a circular constraint.</summary>
         *
         * <param name="lines">Lines defining the linear constraints.</param>
         * <param name="numObstLines">Count of obstacle lines.</param>
         * <param name="beginLine">The line on which the 2-d linear program
         * failed.</param>
         * <param name="radius">The radius of the circular constraint.</param>
         * <param name="result">A reference to the result of the linear program.
         * </param>
         */
        private void linearProgram3(ReadOnlySpan<Line> lines, int numObstLines, int beginLine, float radius, ref float2 result)
        {
            Span<Line> projLineBuffer = stackalloc Line[NeighborsLimit * NeighborsLimit];

            float distance = 0.0f;

            for (int i = beginLine; i < lines.Length; ++i)
            {
                if (det(lines[i].direction, lines[i].point - result) > distance)
                {
                    int count = 0;
                    /* Result does not satisfy constraint of line i. */
                    for (int ii = 0; ii < numObstLines; ++ii)
                    {
                        projLineBuffer[count++] = lines[ii];
                    }
                    for (int j = numObstLines; j < i; ++j)
                    {
                        Line line;

                        float determinant = det(lines[i].direction, lines[j].direction);

                        if (math.abs(determinant) <= RVO_EPSILON)
                        {
                            /* Line i and line j are parallel. */
                            if (math.dot(lines[i].direction, lines[j].direction) > 0.0f)
                            {
                                /* Line i and line j point in the same direction. */
                                continue;
                            }
                            else
                            {
                                /* Line i and line j point in opposite direction. */
                                line.point = 0.5f * (lines[i].point + lines[j].point);
                            }
                        }
                        else
                        {
                            line.point = lines[i].point + (det(lines[j].direction, lines[i].point - lines[j].point) / determinant) * lines[i].direction;
                        }

                        line.direction = math.normalize(lines[j].direction - lines[i].direction);
                        projLineBuffer[count++] = line;
                    }

                    float2 tempResult = result;
                    if (linearProgram2(projLineBuffer.Slice(0, count), radius, new float2(-lines[i].direction.y, lines[i].direction.x), true, ref result) < count)
                    {
                        /*
                         * This should in principle not happen. The result is by
                         * definition already in the feasible region of this
                         * linear program. If it fails, it is due to small
                         * floating point error, and the current result is kept.
                         */
                        result = tempResult;
                    }

                    distance = det(lines[i].direction, lines[i].point - result);
                }
            }
        }
    }
}
