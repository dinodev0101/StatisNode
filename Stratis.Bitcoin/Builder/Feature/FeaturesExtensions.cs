﻿using System.Collections.Generic;
using System.Linq;

namespace Stratis.Bitcoin.Builder.Feature
{
    /// <summary>
    /// Extensions to features collection.
    /// </summary>
    public static class FeaturesExtensions
    {
        /// <summary>
        /// Ensures a dependency feature type is present in the feature registration.
        /// </summary>
        /// <typeparam name="T">The dependency feature type.</typeparam>
        /// <param name="features">List of feature registrations.</param>
        /// <returns>List of feature registrations.</returns>
        /// <exception cref="MissingDependencyException">Thrown if feature type is missing.</exception>
        public static IEnumerable<IFullNodeFeature> EnsureFeature<T>(this IEnumerable<IFullNodeFeature> features)
        {
            if (!features.Any(i => i.GetType() == typeof(T)))
            {
                throw new MissingDependencyException($"Dependency feature {typeof(T)} cannot be found.");
            }

            return features;
        }
    }
}