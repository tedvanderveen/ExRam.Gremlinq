using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using LanguageExt;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ExRam.Gremlinq
{
    public static class GremlinQuery
    {
        private class GremlinQueryImpl : IGremlinQuery
        {
            public GremlinQueryImpl(string graphName, IImmutableList<GremlinStep> steps, IGremlinQueryProvider provider, IImmutableDictionary<MemberInfo, string> memberInfoMappings, IIdentifierFactory identifierFactory)
            {
                this.Steps = steps;
                this.Provider = provider;
                this.GraphName = graphName;
                this.MemberInfoMappings = memberInfoMappings;
                this.IdentifierFactory = identifierFactory;
            }

            public (string queryString, IDictionary<string, object> parameters) Serialize(IParameterCache parameterCache, bool inlineParameters)
            {
                var parameters = new Dictionary<string, object>();
                var builder = new StringBuilder(this.GraphName);

                foreach (var unresolvedStep in this.Steps)
                {
                    foreach (var resolvedStep in unresolvedStep.Resolve(this.Provider.Model))
                    {
                        var appendComma = false;

                        if (builder.Length > 0)
                            builder.Append('.');

                        builder.Append(resolvedStep.Name);
                        builder.Append("(");

                        foreach (var parameter in resolvedStep.Parameters)
                        {
                            if (appendComma)
                                builder.Append(", ");
                            else
                                appendComma = true;

                            if (parameter is IGremlinSerializable serializable)
                            {
                                var (innerQueryString, innerParameters) = serializable.Serialize(parameterCache, inlineParameters);

                                builder.Append(innerQueryString);

                                foreach (var kvp in innerParameters)
                                {
                                    parameters[kvp.Key] = kvp.Value;
                                }
                            }
                            else
                            {
                                if (parameter is string && inlineParameters)
                                    builder.Append($"'{parameter}'");
                                else
                                {
                                    if (inlineParameters)
                                        builder.Append(parameter);
                                    else
                                    {
                                        var newParameterName = parameterCache.Cache(parameter);
                                        parameters[newParameterName] = parameter;

                                        builder.Append(newParameterName);
                                    }
                                }
                            }
                        }

                        builder.Append(")");
                    }
                }

                return (builder.ToString(), parameters);
            }

            public string GraphName { get; }
            public IGremlinQueryProvider Provider { get; }
            public IImmutableList<GremlinStep> Steps { get; }
            public IIdentifierFactory IdentifierFactory { get; }
            public IImmutableDictionary<MemberInfo, string> MemberInfoMappings { get; }
        }

        private sealed class GremlinQueryImpl<T> : GremlinQueryImpl, IGremlinQuery<T>
        {
            public GremlinQueryImpl(string graphName, IImmutableList<GremlinStep> steps, IGremlinQueryProvider provider, IImmutableDictionary<MemberInfo, string> memberInfoMappings, IIdentifierFactory identifierFactory) : base(graphName, steps, provider, memberInfoMappings, identifierFactory)
            {
            }
        }

        public static IGremlinQuery Create(string initialIdentifier, IGremlinQueryProvider provider)
        {
            return new GremlinQueryImpl(initialIdentifier, ImmutableList<GremlinStep>.Empty, provider, ImmutableDictionary<MemberInfo, string>.Empty, IdentifierFactory.CreateDefault());
        }

        public static IGremlinQuery<T> ToAnonymous<T>(this IGremlinQuery<T> query)
        {
            return new GremlinQueryImpl<T>("__", ImmutableList<GremlinStep>.Empty, query.Provider, query.MemberInfoMappings, query.IdentifierFactory);
        }

        public static (string queryString, IDictionary<string, object> parameters) Serialize<T>(this IGremlinQuery<T> query, bool inlineParameters)
        {
            return query.Serialize(new DefaultParameterCache(), inlineParameters);
        }

        public static Task<T> FirstAsync<T>(this IGremlinQuery<T> query)
        {
            return query
                .Limit(1)
                .Execute()
                .First();
        }

        public static async Task<Option<T>> FirstOrNoneAsync<T>(this IGremlinQuery<T> query)
        {
            var array = await query
                .Limit(1)
                .Execute()
                .ToArray();

            return array.Length > 0
                ? array[0]
                : Option<T>.None;
        }

        public static Task<T[]> ToArrayAsync<T>(this IGremlinQuery<T> query)
        {
            return query
                .Execute()
                .ToArray();
        }

        public static IGremlinQuery<T> AddStep<T>(this IGremlinQuery query, string name, params object[] parameters)
        {
            return query.AddStep<T>(new GremlinStep(name, parameters));
        }

        public static IGremlinQuery<T> AddStep<T>(this IGremlinQuery query, GremlinStep step)
        {
            return new GremlinQueryImpl<T>(query.GraphName, query.Steps.Add(step), query.Provider, query.MemberInfoMappings, query.IdentifierFactory);
        }

        public static IGremlinQuery<T> AddStep<T>(this IGremlinQuery query, string name, ImmutableList<object> parameters)
        {
            return query.AddStep<T>(new GremlinStep(name, parameters));
        }

        public static IGremlinQuery<T> Cast<T>(this IGremlinQuery query)
        {
            return new GremlinQueryImpl<T>(query.GraphName, query.Steps, query.Provider, query.MemberInfoMappings, query.IdentifierFactory);
        }
        
        internal static IGremlinQuery<T> AddMemberInfoMapping<T>(this IGremlinQuery<T> query, Expression<Func<T, object>> memberExpression, string mapping)
        {
            var body = memberExpression.Body;
            if (body is UnaryExpression && body.NodeType == ExpressionType.Convert)
                body = ((UnaryExpression)body).Operand;

            var memberExpressionBody = body as MemberExpression;
            if (memberExpressionBody == null)
                throw new ArgumentException();

            return new GremlinQueryImpl<T>(query.GraphName, query.Steps, query.Provider, query.MemberInfoMappings.SetItem(memberExpressionBody.Member, mapping), query.IdentifierFactory);
        }

        internal static IGremlinQuery ReplaceProvider(this IGremlinQuery query, IGremlinQueryProvider provider)
        {
            return new GremlinQueryImpl(query.GraphName, query.Steps, provider, query.MemberInfoMappings, query.IdentifierFactory);
        }

        internal static IGremlinQuery<T> ReplaceProvider<T>(this IGremlinQuery<T> query, IGremlinQueryProvider provider)
        {
            return new GremlinQueryImpl<T>(query.GraphName, query.Steps, provider, query.MemberInfoMappings, query.IdentifierFactory);
        }
    }
}