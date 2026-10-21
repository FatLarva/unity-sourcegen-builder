using System.Collections.Generic;
using UnityEngine;

namespace SourceGenBuilder.Editor
{
    /// <summary>
    /// Project-level settings asset that holds the ordered list of
    /// <see cref="SourceGeneratorProfile"/> assets to build.
    /// Create via Assets → Create → Source Gen Builder → Settings.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SourceGenBuilderSettings",
        menuName = "Source Gen Builder/Settings",
        order = 199)]
    public class SourceGenBuilderSettings : ScriptableObject
    {
        [Tooltip("Ordered list of generator profiles. Built top-to-bottom.")]
        [SerializeField]
        private List<SourceGeneratorProfile> _profiles = new();

        public IReadOnlyList<SourceGeneratorProfile> Profiles => _profiles;
    }
}
