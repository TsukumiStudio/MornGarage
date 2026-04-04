using UnityEngine;

namespace MornLib.Hit2d
{
    public sealed class MornHit2dCircleMono : MornHit2dMono
    {
        [SerializeField] private float _radius;

        protected override int OverlapImpl(Collider2D[] results, LayerMask layerMask)
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(layerMask);
            filter.useTriggers = true;
            return Physics2D.OverlapCircle(transform.position, _radius, filter, results);
        }

        protected override void DrawGizmosImpl()
        {
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}