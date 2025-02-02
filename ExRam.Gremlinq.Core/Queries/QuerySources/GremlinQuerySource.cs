﻿using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using ExRam.Gremlinq.Core.GraphElements;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExRam.Gremlinq.Core
{
    public static class GremlinQuerySource
    {
        private sealed class ConfigurableGremlinQuerySourceImpl : IConfigurableGremlinQuerySource
        {
            private IGremlinQuery _startQuery;
            private readonly bool _isUserSetModel;

            public ConfigurableGremlinQuerySourceImpl(string name, IGraphModel model, GremlinqOptions gremlinqOptions, bool isUserSetModel, IGremlinQueryExecutionPipeline pipeline, ImmutableList<IGremlinQueryStrategy> includedStrategies, ImmutableList<string> excludedStrategies, ILogger logger)
            {
                Name = name;
                Model = model;
                Logger = logger;
                Options = gremlinqOptions;
                Pipeline = pipeline;
                _isUserSetModel = isUserSetModel;
                IncludedStrategies = includedStrategies;
                ExcludedStrategyNames = excludedStrategies;
            }

            IVertexGremlinQuery<TVertex> IGremlinQuerySource.AddV<TVertex>(TVertex vertex)
            {
                return Create()
                    .AddV(vertex);
            }

            IEdgeGremlinQuery<TEdge> IGremlinQuerySource.AddE<TEdge>(TEdge edge)
            {
                return Create()
                    .AddE(edge);
            }

            IVertexGremlinQuery<IVertex> IGremlinQuerySource.V(params object[] ids)
            {
                return Create()
                    .V(ids);
            }

            IEdgeGremlinQuery<IEdge> IGremlinQuerySource.E(params object[] ids)
            {
                return Create()
                    .E(ids);
            }

            IGremlinQuery<TElement> IGremlinQuerySource.Inject<TElement>(params TElement[] elements)
            {
                return Create()
                    .Cast<TElement>()
                    .Inject(elements);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.UseName(string name)
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException($"Invalid value for {nameof(name)}.", nameof(name));

                return new ConfigurableGremlinQuerySourceImpl(name, Model, Options, _isUserSetModel, Pipeline, IncludedStrategies, ExcludedStrategyNames, Logger);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.UseLogger(ILogger logger)
            {
                var newModel = _isUserSetModel
                    ? Model
                    : GraphModel.Dynamic(NullLogger.Instance);

                return new ConfigurableGremlinQuerySourceImpl(Name, newModel, Options, _isUserSetModel, Pipeline, IncludedStrategies, ExcludedStrategyNames, logger);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.AddStrategies(params IGremlinQueryStrategy[] strategies)
            {
                return new ConfigurableGremlinQuerySourceImpl(Name, Model, Options, _isUserSetModel, Pipeline, IncludedStrategies.AddRange(strategies), ExcludedStrategyNames, Logger);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.RemoveStrategies(params string[] strategies)
            {
                return new ConfigurableGremlinQuerySourceImpl(Name, Model, Options, _isUserSetModel, Pipeline, IncludedStrategies, ExcludedStrategyNames.AddRange(strategies), Logger);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.ConfigureOptions(Func<IGremlinQueryEnvironment, GremlinqOptions, GremlinqOptions> optionsTransformation)
            {
                return new ConfigurableGremlinQuerySourceImpl(Name, Model, optionsTransformation(this, Options), _isUserSetModel, Pipeline, IncludedStrategies, ExcludedStrategyNames, Logger);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.ConfigureModel(Func<IGremlinQueryEnvironment, IGraphModel, IGraphModel> modelTransformation)
            {
                return new ConfigurableGremlinQuerySourceImpl(Name, modelTransformation(this, Model), Options, true, Pipeline, IncludedStrategies, ExcludedStrategyNames, Logger);
            }

            IConfigurableGremlinQuerySource IConfigurableGremlinQuerySource.ConfigureExecutionPipeline(Func<IGremlinQueryEnvironment, IGremlinQueryExecutionPipeline, IGremlinQueryExecutionPipeline> pipelineTransformation)
            {
                return new ConfigurableGremlinQuerySourceImpl(Name, Model, Options, true, pipelineTransformation(this, Pipeline), IncludedStrategies, ExcludedStrategyNames, Logger);
            }

            private IGremlinQuery Create()
            {
                var startQuery = Volatile.Read(ref _startQuery);
                if (startQuery != null)
                    return startQuery;

                IGremlinQuery ret = GremlinQuery.Create<Unit>(
                    Name,
                    this);

                if (!ExcludedStrategyNames.IsEmpty)
                    ret = ret.AddStep(new WithoutStrategiesStep(ExcludedStrategyNames.ToArray()));

                foreach (var strategy in IncludedStrategies)
                {
                    ret = strategy.Apply(ret);
                }

                return Interlocked.CompareExchange(ref _startQuery, ret, null) ?? ret;
            }

            public string Name { get; }
            public ILogger Logger { get; }
            public GremlinqOptions Options { get; }
            public IGraphModel Model { get; }
            public IGremlinQueryExecutionPipeline Pipeline { get; }
            public ImmutableList<string> ExcludedStrategyNames { get; }
            public ImmutableList<IGremlinQueryStrategy> IncludedStrategies { get; }
        }

        // ReSharper disable once InconsistentNaming
        #pragma warning disable IDE1006 // Naming Styles
        public static readonly IConfigurableGremlinQuerySource g = Create();
        #pragma warning restore IDE1006 // Naming Styles
    
        public static IConfigurableGremlinQuerySource Create(string name = "g")
        {
            return new ConfigurableGremlinQuerySourceImpl(
                name,
                GraphModel.Dynamic(NullLogger.Instance),
                default,
                false,
                GremlinQueryExecutionPipeline.Empty,
                ImmutableList<IGremlinQueryStrategy>.Empty,
                ImmutableList<string>.Empty,
                NullLogger.Instance);
        }

        public static IEdgeGremlinQuery<TEdge> AddE<TEdge>(this IGremlinQuerySource source) where TEdge : new()
        {
            return source.AddE(new TEdge());
        }

        public static IVertexGremlinQuery<TVertex> AddV<TVertex>(this IGremlinQuerySource source) where TVertex : new()
        {
            return source.AddV(new TVertex());
        }

        public static IConfigurableGremlinQuerySource UseModel(this IConfigurableGremlinQuerySource source, IGraphModel model)
        {
            return source.ConfigureModel(_ => model);
        }

        public static IVertexGremlinQuery<TNewVertex> ReplaceV<TNewVertex>(this IGremlinQuerySource source, TNewVertex vertex)
        {
            return source
                .V<TNewVertex>(vertex.GetId())
                .Update(vertex);
        }

        public static IEdgeGremlinQuery<TNewEdge> ReplaceE<TNewEdge>(this IGremlinQuerySource source, TNewEdge edge)
        {
            return source
                .E<TNewEdge>(edge.GetId())
                .Update(edge);
        }

        public static IEdgeGremlinQuery<TEdge> E<TEdge>(this IGremlinQuerySource source, params object[] ids)
        {
            return source.E(ids).OfType<TEdge>();
        }

        public static IVertexGremlinQuery<TVertex> V<TVertex>(this IGremlinQuerySource source, params object[] ids)
        {
            return source.V(ids).OfType<TVertex>();
        }

        public static IConfigurableGremlinQuerySource ConfigureOptions(this IConfigurableGremlinQuerySource source, Func<GremlinqOptions, GremlinqOptions> optionsTransformation)
        {
            return source.ConfigureOptions((_, o) => optionsTransformation(o));
        }

        public static IConfigurableGremlinQuerySource ConfigureModel(this IConfigurableGremlinQuerySource source, Func<IGraphModel, IGraphModel> modelTransformation)
        {
            return source.ConfigureModel((_, m) => modelTransformation(m));
        }

        public static IConfigurableGremlinQuerySource ConfigureExecutionPipeline(this IConfigurableGremlinQuerySource source, Func<IGremlinQueryExecutionPipeline, IGremlinQueryExecutionPipeline> pipelineTransformation)
        {
            return source.ConfigureExecutionPipeline((_, b) => pipelineTransformation(b));
        }

        public static IConfigurableGremlinQuerySource UseExecutionPipeline(this IConfigurableGremlinQuerySource source, IGremlinQueryExecutionPipeline pipeline)
        {
            return source.ConfigureExecutionPipeline((_, __) => pipeline);
        }
    }
}
