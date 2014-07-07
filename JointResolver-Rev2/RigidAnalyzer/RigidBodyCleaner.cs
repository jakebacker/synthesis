﻿using Microsoft.VisualBasic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Inventor;

static class RigidBodyCleaner
{
    /// <summary>
    /// Removes all the meaningless items from the given group of rigid body results, simplifying the model.
    /// </summary>
    /// <remarks>
    /// Meaningless items include...
    /// <list type="bullet">
    /// <item><description>Component occurrences that are suppressed</description></item>
    /// <item><description>Suppressed joints</description></item>
    /// <item><description>Joints with suppressed components</description></item>
    /// <item><description>Rigid joints between the same object</description></item>
    /// <item><description>Rigid joints with meaningless groups</description></item>
    /// <item><description>Rigid joints with not assembly constraints or joints</description></item>
    /// <item><description>Rigid groups with no objects</description></item>
    /// </list>
    /// </remarks>
    /// <param name="results">Rigid results to clean</param>
    public static void CleanMeaningless(CustomRigidResults results)
    {
        foreach (CustomRigidGroup group in results.groups)
        {
            group.occurrences.RemoveAll(item => item.Suppressed);
        }
        foreach (CustomRigidJoint joint in results.joints)
        {
            joint.joints.RemoveAll(item => item.OccurrenceOne.Suppressed || item.OccurrenceTwo.Suppressed || item.Suppressed);
            joint.constraints.RemoveAll(item => item.OccurrenceOne.Suppressed || item.OccurrenceTwo.Suppressed || item.Suppressed);
        }
        results.joints.RemoveAll(item => item.groupOne.Equals(item.groupTwo) || item.groupOne.occurrences.Count <= 0 || item.groupTwo.occurrences.Count <= 0 || (item.joints.Count + item.constraints.Count) <= 0);
        results.groups.RemoveAll(item => item.occurrences.Count <= 0);
    }

    /// <summary>
    /// Merges all the grounded bodies within the given rigid results into a single group. 
    /// </summary>
    /// <remarks>
    /// This also updates the references of all the rigid joints to the new groups.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No grounded bodies exist to merge.</exception>
    /// <param name="results">Rigid results to clean</param>
    public static void CleanGroundedBodies(CustomRigidResults results)
    {
        CustomRigidGroup firstRoot = null;
        foreach (CustomRigidGroup group in results.groups)
        {
            if (group.grounded)
            {
                if ((firstRoot == null))
                {
                    firstRoot = group;
                }
                else
                {
                    firstRoot.occurrences.AddRange(group.occurrences);
                    group.occurrences.Clear();
                }
            }
        }
        if (firstRoot != null)
        {
            foreach (CustomRigidJoint joint in results.joints)
            {
                if (joint.groupOne.occurrences.Count == 0)
                {
                    joint.groupOne = firstRoot;
                }
                if (joint.groupTwo.occurrences.Count == 0)
                {
                    joint.groupTwo = firstRoot;
                }
            }
        }
        else
        {
            throw new InvalidOperationException("No ground!");
        }
        CleanMeaningless(results);
    }

    /// <summary>
    /// Generates a mapping between each rigid group and all the other groups connected to it.
    /// </summary>
    /// <param name="results">The rigid results to generate joint maps from.</param>
    /// <param name="joints">A mapping between each rigid group and a set of rigid groups connected by a joint.</param>
    /// <param name="constraints">A mapping between each rigid group and a set of rigid groups connected by constraints.</param>
    private static void GenerateJointMaps(CustomRigidResults results, Dictionary<CustomRigidGroup, HashSet<CustomRigidGroup>> joints, Dictionary<CustomRigidGroup, HashSet<CustomRigidGroup>> constraints)
    {
        foreach (CustomRigidGroup group in results.groups)
        {
            joints.Add(group, new HashSet<CustomRigidGroup>());
            constraints.Add(group, new HashSet<CustomRigidGroup>());
        }
        foreach (CustomRigidJoint j in results.joints)
        {
            if (j.joints.Count > 0 && j.joints[0].Definition.JointType != AssemblyJointTypeEnum.kRigidJointType)
            {
                joints[j.groupOne].Add(j.groupTwo);
                joints[j.groupTwo].Add(j.groupOne);
            }
            else if (j.constraints.Count > 0 || j.joints.Count > 1 && (j.joints[0].Definition.JointType == AssemblyJointTypeEnum.kRigidJointType))
            {
                constraints[j.groupOne].Add(j.groupTwo);
                constraints[j.groupTwo].Add(j.groupOne);
            }
        }
    }

    private class PlannedJoint
    {
        public RigidNode node;
        public RigidNode parentNode;
        public CustomRigidJoint joint;
    }

    /// <summary>
    /// Merges any groups that are connected only with constraints and generate a node tree.
    /// </summary>
    /// <remarks>
    /// This starts at whichever rigid group is grounded, then branches out along rigid joints from there.
    /// If the rigid joint is movable (made of assembly joint(s)) then another node is created, if the joint
    /// is constraint-only then the leaf node is merged into the current branch.
    /// </remarks>
    /// <param name="results">Rigid results to clean</param>
    public static RigidNode BuildAndCleanDijkstra(CustomRigidResults results)
    {
        Dictionary<CustomRigidGroup, HashSet<CustomRigidGroup>> constraints = new Dictionary<CustomRigidGroup, HashSet<CustomRigidGroup>>();
        Dictionary<CustomRigidGroup, HashSet<CustomRigidGroup>> joints = new Dictionary<CustomRigidGroup, HashSet<CustomRigidGroup>>();
        GenerateJointMaps(results, joints, constraints);

        // Mapping rigid group to merge-into group
        Dictionary<CustomRigidGroup, CustomRigidGroup> mergePattern = new Dictionary<CustomRigidGroup, CustomRigidGroup>();
        // Mapping rigid group to skeletal node
        Dictionary<CustomRigidGroup, RigidNode> baseNodes = new Dictionary<CustomRigidGroup, RigidNode>();
        // Deffered joint creation.  Required so merge can take place.
        List<PlannedJoint> plannedJoints = new List<PlannedJoint>();
        // The base of the skeletal tree
        RigidNode baseRoot = null;

        // All the currently open groups as an array {currentGroup, mergeIntoGroup}
        List<CustomRigidGroup[]> openNodes = new List<CustomRigidGroup[]>();
        // All the groups that have been processed.  (Closed nodes)
        HashSet<CustomRigidGroup> closedNodes = new HashSet<CustomRigidGroup>();

        // Find the first grounded group, the start point for dijkstra's algorithm.
        foreach (CustomRigidGroup grp in results.groups)
        {
            if (grp.grounded)
            {
                openNodes.Add(new CustomRigidGroup[] { grp, grp });
                closedNodes.Add(grp);
                baseNodes.Add(grp, baseRoot = new RigidNode(grp));
                break;
            }
        }
        Console.WriteLine("Determining merge commands");
        while (openNodes.Count > 0)
        {
            List<CustomRigidGroup[]> newOpen = new List<CustomRigidGroup[]>();
            foreach (CustomRigidGroup[] node in openNodes)
            {
                // Get all connections
                HashSet<CustomRigidGroup> cons = constraints[node[0]];
                HashSet<CustomRigidGroup> jons = joints[node[0]];
                foreach (CustomRigidGroup jonConn in jons)
                {
                    if (!closedNodes.Add(jonConn)) continue;
                    RigidNode rnode = new RigidNode(jonConn);
                    baseNodes.Add(jonConn, rnode);
                    // Get joint name
                    CustomRigidJoint joint = null;
                    foreach (CustomRigidJoint jnt in results.joints)
                    {
                        if (jnt.joints.Count > 0 && ((jnt.groupOne.Equals(jonConn) && jnt.groupTwo.Equals(node[0]))
                            || (jnt.groupOne.Equals(node[0]) && jnt.groupTwo.Equals(jonConn))))
                        {
                            joint = jnt;
                            break;
                        }
                    }
                    if (joint != null)
                    {
                        PlannedJoint pJoint = new PlannedJoint();
                        pJoint.joint = joint;
                        pJoint.parentNode = baseNodes[node[1]];
                        pJoint.node = rnode;
                        plannedJoints.Add(pJoint);
                        newOpen.Add(new CustomRigidGroup[] { jonConn, jonConn });
                    }
                }
                foreach (CustomRigidGroup consConn in cons)
                {
                    if (!closedNodes.Add(consConn)) continue;
                    mergePattern.Add(consConn, node[1]);
                    newOpen.Add(new CustomRigidGroup[] { consConn, node[1] });
                }
            }
            openNodes = newOpen;
        }

        Console.WriteLine("Do " + mergePattern.Count + " merge commands");
        foreach (KeyValuePair<CustomRigidGroup, CustomRigidGroup> pair in mergePattern)
        {
            pair.Value.occurrences.AddRange(pair.Key.occurrences);
            pair.Key.occurrences.Clear();
            pair.Value.grounded = pair.Value.grounded || pair.Key.grounded;
        }
        Console.WriteLine("Resolve broken joints");
        foreach (CustomRigidJoint joint in results.joints)
        {
            CustomRigidGroup thing = null;
            if (mergePattern.TryGetValue(joint.groupOne, out thing))
            {
                joint.groupOne = thing;
            }
            if (mergePattern.TryGetValue(joint.groupTwo, out thing))
            {
                joint.groupTwo = thing;
            }
        }
        Console.WriteLine("Creating planned skeletal joints");
        foreach (PlannedJoint pJoint in plannedJoints)
        {
            SkeletalJoint_Base sJ = SkeletalJoint.Create(pJoint.joint, pJoint.parentNode.group);
            pJoint.parentNode.AddChild(sJ, pJoint.node);
        }
        Console.WriteLine("Cleanup remainders");
        CleanMeaningless(results);
        return baseRoot;
    }
}