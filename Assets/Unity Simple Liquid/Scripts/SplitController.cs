﻿#pragma warning disable 0649

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnitySimpleLiquid
{
    /// <summary>
    /// Calculates liquids splitting effect and transfer to other liquid containers
    /// </summary>
    public class SplitController : MonoBehaviour
    {
        public LiquidContainer liquidContainer;
        [SerializeField]
        private float bottleneckRadius = 0.1f;
        public float BottleneckRadiusWorld { get; private set; }
		
        [Tooltip("How fast liquid split from container")]
        public float splitSpeed = 2f;
		[Tooltip("Number number of objects the liquid will hit off and continue flowing")]
		public int maxEdgeDrops = 4;
		private int currentDrop;
		[Tooltip("Number of objects the raycast is able to store, increase if objects / containers are not found")]
		public int rayCastBufferSize = 10;
		private RaycastHit[] rayCastBuffer;
		public ParticleSystem particlesPrefab;

		private void Start()
		{
			rayCastBuffer = new RaycastHit[rayCastBufferSize];
		}

		#region Particles
		private ParticleSystem particles;
        public ParticleSystem Particles
        {
            get
            {
                if (!particlesPrefab)
                    return null;

                if (!particles)
                    particles = Instantiate(particlesPrefab, transform);
                return particles;
            }
        }

        private void StartEffect(Vector3 splitPos, float scale)
        {
            var particlesInst = Particles;
            if (!particlesInst)
                return;

            var mainModule = particlesInst.main;
            mainModule.startColor = liquidContainer.LiquidColor;

            particlesInst.transform.localScale = Vector3.one * BottleneckRadiusWorld * scale;
            particlesInst.transform.position = splitPos;
            particlesInst.Play();
        }
        #endregion

        #region Bottleneck
        public Plane bottleneckPlane { get; private set; }
        public Plane surfacePlane { get; private set; }
        public Vector3 BottleneckPos { get; private set; }

        private Plane GenerateBottleneckPlane()
        {
            if (!liquidContainer)
                return new Plane();

            var mesh = liquidContainer.LiquidMesh;
            if (!mesh)
                return new Plane();

            var max = mesh.bounds.max.y;
            return new Plane(liquidContainer.transform.up,
                max * liquidContainer.transform.lossyScale.y);
        }

        private Vector3 GenerateBottleneckPos()
        {
            if (!liquidContainer)
                return Vector3.zero;

            var tr = liquidContainer.transform;
            var pos = bottleneckPlane.normal * bottleneckPlane.distance + tr.position;
            return pos;
        }

        private Vector3 GenerateBottleneckLowesPoint()
        {
            if (!liquidContainer)
                return Vector3.zero;

            // TODO: This code is not optimal and can be done much better
            // Righ now it caluclates minimal point of the circle (in 3d) by brute force
            var containerOrientation = liquidContainer.transform.rotation;

            // Points on bottleneck radius (local space)
            var angleStep = 0.1f;            
			Vector3 min = Vector3.positiveInfinity; //really high vector
			Vector3 tmpPoint;

            for (float a = 0; a < Mathf.PI * 2f; a += angleStep)
            {

				//Get local point				
				tmpPoint.x = BottleneckRadiusWorld * Mathf.Cos(a);
				tmpPoint.y = 0;
				tmpPoint.z = BottleneckRadiusWorld * Mathf.Sin(a);
				//Transform to world point
				tmpPoint = BottleneckPos + containerOrientation * tmpPoint;
				//Was it smaller than last one?
				if (tmpPoint.y < min.y)
					min = tmpPoint;
            }

            return min;

        }
        #endregion

        #region Gizmos
        private void OnDrawGizmosSelected()
        {
            // Draws bottleneck direction and radius
            var bottleneckPlane = GenerateBottleneckPlane();
            BottleneckRadiusWorld = bottleneckRadius * transform.lossyScale.magnitude;

            Gizmos.color = Color.red;
            GizmosHelper.DrawPlaneGizmos(bottleneckPlane, transform);

            // And bottleneck position
            GizmosHelper.DrawSphereOnPlane(bottleneckPlane, BottleneckRadiusWorld, transform);			
		}
		private void OnDrawGizmos()
		{
			// Draw a yellow sphere at the transform's position
            if (raycasthit != Vector3.zero)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(raycasthit, 0.01f);
				Gizmos.DrawSphere(raycastStart, 0.01f);
			}
		}
		#endregion

		#region Split Logic
		private const float splashSize = 0.025f;

        public bool IsSpliting { get; private set; }

        private void CheckSpliting()
        {
            IsSpliting = false;

            if (liquidContainer == null)
                return;

            // Do we have something to split?
            if (liquidContainer.FillAmountPercent <= 0f)
                return;
            if (!liquidContainer.IsOpen)
                return;

            // Check if liquid is overflows
            Vector3 overflowsPoint, lineVec;
            var overflows = GeomUtils.PlanePlaneIntersection(out overflowsPoint, out lineVec,
                bottleneckPlane, surfacePlane);

            // Translate to contrainers world position
            overflowsPoint += liquidContainer.transform.position;

            if (overflows)
            {
                // Let's check if overflow point is inside bottleneck radius
                var insideBottleneck = Vector3.Distance(overflowsPoint, BottleneckPos) < BottleneckRadiusWorld;

                if (insideBottleneck)
                {
                    // We are inside bottleneck - just start spliting from lowest bottleneck point
                    var minPoint = GenerateBottleneckLowesPoint();
                    SplitLogic(minPoint);
                    return;
                }
            }

            if (BottleneckPos.y < overflowsPoint.y)
            {
                // Oh, looks like container is upside down - let's check it
                var dot = Vector3.Dot(bottleneckPlane.normal, surfacePlane.normal);
                if (dot < 0f)
                {
                    // Yep, let's split from the bottleneck center
                    SplitLogic(BottleneckPos);
                }
                else
                {
                    // Well, this weird, let's check if spliting point is even inside our liquid
                    var dist = liquidContainer.liquidRender.bounds.SqrDistance(overflowsPoint);
                    var inBounding = dist < 0.0001f;

                    if (inBounding)
                    {
                        // Yeah, we are inside liquid container
                        var minPoint = GenerateBottleneckLowesPoint();
                        SplitLogic(minPoint);
                    }
                }
            }
        }

        private void SplitLogic(Vector3 splitPos)
        {
            IsSpliting = true;

            // Check rotation of liquid container
            // It conttolls how many liquid we lost and particles size
            var howLow = Vector3.Dot(Vector3.up, liquidContainer.transform.up);
            var flowScale = 1f - (howLow + 1) * 0.5f + 0.2f;

            var liquidStep = BottleneckRadiusWorld * splitSpeed * Time.deltaTime * flowScale;
            var newLiquidAmmount = liquidContainer.FillAmountPercent - liquidStep;

            // Check if amount is negative and change it to zero
            if (newLiquidAmmount < 0f)
            {
                liquidStep = liquidContainer.FillAmountPercent;
                newLiquidAmmount = 0f;
            }

            // Transfer liquid to other container (if possible)
            liquidContainer.FillAmountPercent = newLiquidAmmount;

			FindLiquidContainer(splitPos, liquidStep, flowScale, this.gameObject);
			
			// Start particles effect
			StartEffect(splitPos, flowScale);
		}


		//Used for Gizmo only
		private Vector3 raycasthit;
		private Vector3 raycastStart;

		private bool TransferLiquid(RaycastHit hit, float lostPercentAmount, float scale)
        {
			SplitController liquid = hit.collider.GetComponent<SplitController>();
			if (liquid != null)
			{
				Vector3 otherBottleneck = liquid.GenerateBottleneckPos();
				float radius = liquid.BottleneckRadiusWorld;

				// Does we touched bottleneck?
				bool insideRadius = Vector3.Distance(hit.point, otherBottleneck) < radius + splashSize * scale;
				if (insideRadius)
				{
					float lostAmount = liquidContainer.Volume * lostPercentAmount;
					liquid.liquidContainer.FillAmount += lostAmount;
					return true;
				}
			}
			return false;
			
		}
		
		private void FindLiquidContainer(Vector3 splitPos, float lostPercentAmount, float scale, GameObject ignoreCollision)
		{
			
			Ray ray = new Ray(splitPos, Vector3.down);

			// Check all colliders under ours
			// Using the non-allocating physics APIs
			// https://docs.unity3d.com/Manual/BestPracticeUnderstandingPerformanceInUnity7.html

			float numberOfHits = Physics.SphereCastNonAlloc(ray, splashSize, rayCastBuffer);

			// Sort the results ourselves
			RaycastHit hit = new RaycastHit
			{
				distance = float.MaxValue
			};
			//We shouldn't need to clear the array since the raycast will fill from buffer[0] and it returns the number of hits
			for (int i=0; i< numberOfHits; i++)
			{								
				if (rayCastBuffer[i].distance < hit.distance && !GameObject.ReferenceEquals(rayCastBuffer[i].collider.gameObject, ignoreCollision) && !rayCastBuffer[i].collider.isTrigger)
				{
					hit = rayCastBuffer[i];
				}
			}

			// Try to transfer liquid to it, if it fails we should roll liquid off the edge of the container or whatever else it might be
			if (!TransferLiquid(hit, lostPercentAmount, scale))
			{
				//Something other than a liquid splitter is in the way

				//If we have already dropped down off too many objects, break
				if (currentDrop < maxEdgeDrops)
				{
					//Simulate the liquid running off an object it hits and continuing down from the edge of the liquid
					//Does not take velocity into account

					//First get the slope direction
					Vector3 slope = GetSlopeDirection(Vector3.up, hit.normal);

					//Next we try to find the edge of the object the liquid would roll off
					//This really only works for primitive objects, it would look weird on other stuff
					Vector3 edgePosition = TryGetSlopeEdge(slope, hit);
					if (edgePosition != Vector3.zero)
					{
						//edge position found, surface must be tilted
						//Now we can try to transfer the liquid from this position
						currentDrop++;
						FindLiquidContainer(edgePosition, lostPercentAmount, scale, hit.collider.gameObject);
					}
				}
			}
		}

		#endregion

		#region Slope Logic
		private float GetIdealRayCastDist(Bounds boundBox, Vector3 point, Vector3 slope)
		{
			Vector3 final = boundBox.min;

			// X axis	
			if (slope.x > 0)
				final.x = boundBox.max.x;
			// Y axis	
			if (slope.y > 0)
				final.y = boundBox.max.y;
			// Z axis
			if (slope.z > 0)
				final.z = boundBox.max.z;

			return Vector3.Distance(point, final);
		}

		private Vector3 GetSlopeDirection(Vector3 up, Vector3 normal)
		{
			//https://forum.unity.com/threads/making-a-player-slide-down-a-slope.469988/#post-3062204			
			return Vector3.Cross(Vector3.Cross(up, normal), normal).normalized;
		}

		private Vector3 moveDown = new Vector3(0f, -0.0001f, 0f);
		private Vector3 TryGetSlopeEdge(Vector3 slope, RaycastHit hit)
		{
			Vector3 edgePosition = Vector3.zero;
			//flip a raycast so it faces backwards towards the object we hit, move it slightly down so it will hit the edge of the object
			
			float dist = GetIdealRayCastDist(hit.collider.bounds, hit.point, slope);

			Vector3 reverseRayPos = hit.point + moveDown + (slope * dist);
			raycastStart = reverseRayPos;
			Ray backwardsRay = new Ray(reverseRayPos, -slope);

			// Using the non-allocating physics APIs
			// https://docs.unity3d.com/Manual/BestPracticeUnderstandingPerformanceInUnity7.html
			// Specifying a distance for this raycast is very important so we dont hit too many objects
			float numberOfHits = Physics.RaycastNonAlloc(backwardsRay, rayCastBuffer);
			RaycastHit thisHit = new RaycastHit
			{
				distance = float.MaxValue
			};
			// To save on the GC which can kill VR, sort the results ourselves			
			for (int i = 0; i < numberOfHits; i++)
			{
				// https://answers.unity.com/questions/752382/how-to-compare-if-two-gameobjects-are-the-same-1.html
				//We only want to get this position on the original object we hit off of
				if (rayCastBuffer[i].distance < thisHit.distance && GameObject.ReferenceEquals(rayCastBuffer[i].collider.gameObject, hit.collider.gameObject))
				{
					//We hit the object the liquid is running down!
					raycasthit = edgePosition = rayCastBuffer[i].point;
					break;
				}				
			}

			return edgePosition;
		}
		#endregion

		private void Update()
        {
            // Update bottleneck and surface from last update
            bottleneckPlane = GenerateBottleneckPlane();
            BottleneckPos = GenerateBottleneckPos();
            surfacePlane = liquidContainer.GenerateSurfacePlane();
            BottleneckRadiusWorld = bottleneckRadius * transform.lossyScale.magnitude;

			// Now check spliting, starting from the top
			currentDrop = 0;
            CheckSpliting();
        }
    }
}