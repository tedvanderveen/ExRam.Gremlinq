﻿using System;

namespace ExRam.Gremlinq.Core
{
    public static class GremlinQuerySerializerBuilderExtensions
    {
        private static readonly Step NoneWorkaround = new NotStep(GremlinQuery.Anonymous(GremlinQueryEnvironment.Default).Identity());

        public static IGremlinQuerySerializerBuilder UseCosmosDbWorkarounds(this IGremlinQuerySerializerBuilder builder)
        {
            return builder
                .OverrideAtomSerializationHandler<SkipStep>((step, assembler, overridden, recurse) => recurse(new RangeStep(step.Count, -1)))
                .OverrideAtomSerializationHandler<NoneStep>((step, assembler, overridden, recurse) => recurse(NoneWorkaround))
                .OverrideAtomSerializationHandler<LimitStep>((step, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    if (step.Count > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(step), "CosmosDb doesn't currently support values for 'Limit' outside the range of a 32-bit-integer.");

                    overridden(step);
                })
                .OverrideAtomSerializationHandler<TailStep>((step, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    if (step.Count > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(step), "CosmosDb doesn't currently support values for 'Tail' outside the range of a 32-bit-integer.");

                    overridden(step);
                })
                .OverrideAtomSerializationHandler<RangeStep>((step, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    if (step.Lower > int.MaxValue || step.Upper > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(step), "CosmosDb doesn't currently support values for 'Range' outside the range of a 32-bit-integer.");

                    overridden(step);
                })
                .OverrideAtomSerializationHandler<long>((l, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    recurse((int)l);
                });
        }
    }
}
