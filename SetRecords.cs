﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.IO;
using Aerospike.Client;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;

namespace Aerospike.Database.LINQPadDriver.Extensions
{
    
    [DebuggerDisplay("{ToString()}")]
    public abstract class SetRecords<T> : SetRecords, IEnumerable<T>
        where T : ARecord
    {
        #region Constructors
        public SetRecords([NotNull] LPSet lpSet,
                            [NotNull] ANamespaceAccess setAccess,
                            [NotNull] string setName,
                            params string[] bins)
            : base(lpSet, setAccess, setName, bins)
        { }

        public SetRecords([NotNull] ANamespaceAccess setAccess,
                           [NotNull] string setName,
                           params string[] bins)
           : base(setAccess, setName, bins)
        { }

        public SetRecords([NotNull] SetRecords<T> clone)
           : base(clone)
        { }

        #endregion

        /// <summary>
        /// Changes how records are displayed using the LinqPad <see cref="LINQPad.Extensions.Dump{T}(T)"/> method.        
        /// </summary>
        /// <param name="newRecordView">See <see cref="ARecord.DumpTypes"/> for more information.</param>
        /// <returns>This instance</returns>
        /// <seealso cref="ARecord.DumpTypes"/>
        /// <seealso cref="SetRecords.DefaultRecordView"/>
        public new SetRecords<T> ChangeRecordView(ARecord.DumpTypes newRecordView)
        {
            this.DefaultRecordView = newRecordView;
            return this;
        }       

        #region Get Methods
        /// <summary>
        /// Returns the record based on the primary key
        /// </summary>
        /// <param name="primaryKey">
        /// The primary key can be a <see cref="Aerospike.Client.Key"/>, <see cref="Aerospike.Client.Value"/>, digest (byte[]), or a .net type.
        /// </param>
        /// <param name="bins">
        /// An optional arguments, if provided only those bins are returned.
        /// </param>
        /// <returns>
        /// A record if the primary key is found otherwise null.
        /// </returns>
        /// <seealso cref="Get(dynamic, Expression, string[])"/>
        public new T Get([NotNull] dynamic primaryKey, params string[] bins)
        {
            Client.Key key = Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName);

            var record = this.SetAccess
                                .AerospikeConnection
                                .AerospikeClient
                                .Get(this.DefaultReadPolicy, key, bins.Length == 0 ? null : bins);

            if (record == null) return null;

            return (T) CreateRecord(this.SetAccess,
                                        key,
                                        record,
                                        this._bins,
                                        this.BinsHashCode,
                                        recordView: this.DefaultRecordView);
        }

        /// <summary>
        /// Returns the record based on the primary key
        /// </summary>
        /// <param name="primaryKey">
        /// The primary key can be a <see cref="Aerospike.Client.Key"/>, <see cref="Aerospike.Client.Value"/>, digest (byte[]), or a .net type.
        /// </param>
        /// <param name="filterExpresion">
        /// A filter expression that is applied after obtaining the record via the primary key.
        /// </param>
        /// <param name="bins">
        /// An optional arguments, if provided only those bins are returned.
        /// </param>
        /// <returns>
        /// A record if the primary key is found otherwise null.
        /// </returns>
        /// <seealso cref="Get(dynamic, string[])"/>
        public new T Get([NotNull] dynamic primaryKey, Expression filterExpresion, params string[] bins)
        {
            Client.Key key = Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName);

            var policy = new Client.Policy(this.DefaultReadPolicy) { filterExp = filterExpresion };

            var record = this.SetAccess
                                .AerospikeConnection
                                .AerospikeClient
                                .Get(policy, key, bins.Length == 0 ? null : bins);

            if (record == null) return null;

            return (T)CreateRecord(this.SetAccess,
                                        key,
                                        record,
                                        this._bins,
                                        this.BinsHashCode,
                                        recordView: this.DefaultRecordView);
        }
        #endregion

        #region Query Methods

        /// <summary>
        /// Returns all the records based on the associated bins.
        /// </summary>
        /// <param name="bins">Only include these bins in the result.</param>
        /// <returns>
        /// A collection of all records
        /// </returns>
        /// <exception cref="AerospikeException">Thrown if an index cannot be found to match the filter</exception>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="Query(Exp, string[])"/>               
        new public IEnumerable<T> Query(params string[] bins)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy);
            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return (T)CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Performs a <see cref="Client.AerospikeClient.Query(QueryPolicy, Statement)"/> applying the expression filter.
        /// </summary>
        /// <param name="filterExpression">
        /// The Aerospike filter <see cref="Client.Exp"/> that will be applied.
        /// <seealso cref="Aerospike.Client.ListExp"/>
        /// <seealso cref="Aerospike.Client.MapExp"/>
        /// <seealso cref="Aerospike.Client.BitExp"/>
        /// <seealso cref="Aerospike.Client.HLLExp"/>
        /// </param>
        /// <param name="bins">Return only the bins provided in the result set</param>
        /// <returns>
        /// The result set based on the expression filter.
        /// </returns>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="Query(string[])"/>
        /// <seealso cref="Operation"/>
        new public IEnumerable<T> Query([NotNull] Client.Exp filterExpression, params string[] bins)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy,
                                           string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                                   ? new Statement() { Namespace = this.Namespace, BinNames = bins }
                                                   : new Statement() { Namespace = this.Namespace, SetName = this.SetName, BinNames = bins });

            while (recordset.Next())
            {
                yield return (T)CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Performs a secondary index query using the provided <see cref="Client.Filter"/>.
        /// </summary>
        /// <param name="secondaryIdxFilter">The filter used against the secondary index</param>
        /// <param name="bins">Only include these bins in the result.</param>
        /// <returns>
        /// A collection of records that match the filter.
        /// </returns>
        /// <exception cref="AerospikeException">Thrown if an index cannot be found to match the filter</exception>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Exp, string[])"/>
        /// <seealso cref="Query(string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        new public IEnumerable<T> Query([NotNull] Client.Filter secondaryIdxFilter, params string[] bins)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy);
            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            stmt.SetFilter(secondaryIdxFilter);
            stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return (T)CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Performs a secondary index query using the provided <see cref="Client.Filter"/> and than apply the filter expression.
        /// </summary>
        /// <param name="secondaryIdxFilter">The filter used against the secondary index</param>
        /// <param name="filterExpression">
        /// The Aerospike filter <see cref="Client.Exp"/> that will be applied after the index filter is applied.
        /// <seealso cref="Aerospike.Client.ListExp"/>
        /// <seealso cref="Aerospike.Client.MapExp"/>
        /// <seealso cref="Aerospike.Client.BitExp"/>
        /// <seealso cref="Aerospike.Client.HLLExp"/>
        /// </param>
        /// <param name="bins">Only include these bins in the result.</param>
        /// <returns>
        /// A collection of records that match the <paramref name="filterExpression"/>.
        /// </returns>
        /// <exception cref="AerospikeException">Thrown if an index cannot be found to match the filter</exception>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="Query(string[])"/>
        /// <seealso cref="Query(Exp, string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        new public IEnumerable<T> Query([NotNull] Client.Filter secondaryIdxFilter, Client.Exp filterExpression, params string[] bins)
        {
            var queryPolicy = filterExpression == null
                                ? this.DefaultQueryPolicy
                                : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            stmt.SetFilter(secondaryIdxFilter);
            stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return (T)CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        #endregion

        #region Linq Type Methods

        /// <summary>
        /// Returns the top number of records from the set based on <see cref="SetRecords.DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="numberRecords">Number of records to return</param>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns>A collection of records or empty set</returns>
        /// <seealso cref="First(Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="SetRecords.Operate(dynamic, Operation[])"/>
        /// <seealso cref="SetRecords.DefaultQueryPolicy"/>
        public new IEnumerable<T> Take(int numberRecords, Client.Exp filterExpression = null)
        {
            if (numberRecords <= 0) yield break;

            var queryPolicy = filterExpression == null
                                    ? this.DefaultQueryPolicy
                                    : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy,
                                           string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                           ? new Statement() { Namespace = this.Namespace, MaxRecords = numberRecords }
                                           : new Statement() { Namespace = this.Namespace, SetName = this.SetName, MaxRecords = numberRecords });

            while (recordset.Next())
            {
                yield return (T) CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Returns the first record from the set based on <see cref="SetRecords.DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns></returns>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="SetRecords.Operate(dynamic, Operation[])"/>
        /// <seealso cref="SetRecords.DefaultQueryPolicy"/>
        public new T First(Client.Exp filterExpression = null)
        {
            return this.Take(1, filterExpression).First();
        }

        /// <summary>
        /// Returns the first record or null from the set based on <see cref="SetRecords.DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns></returns>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="SetRecords.Operate(dynamic, Operation[])"/>
        /// <seealso cref="SetRecords.DefaultQueryPolicy"/>
        public new T FirstOrDefault(Client.Exp filterExpression = null)
        {
            return this.Take(1, filterExpression).FirstOrDefault();
        }

        /// <summary>
        /// Skips the number of records from the set based on <see cref="SetRecords.DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="numberRecords">Number of records to skip</param>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns></returns>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="SetRecords.Operate(dynamic, Operation[])"/>
        /// <seealso cref="SetRecords.DefaultQueryPolicy"/>
        public new IEnumerable<T> Skip(int numberRecords, Client.Exp filterExpression = null)
        {
            int currentIdx = 0;

            var queryPolicy = filterExpression == null
                                    ? this.DefaultQueryPolicy
                                    : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy,
                                           string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                           ? new Statement() { Namespace = this.Namespace }
                                           : new Statement() { Namespace = this.Namespace, SetName = this.SetName });

            while (recordset.Next())
            {
                if (++currentIdx > numberRecords)
                    yield return (T) CreateRecord(this.SetAccess,
                                                    recordset.Key,
                                                    recordset.Record,
                                                    this._bins,
                                                    this.BinsHashCode,
                                                    recordView: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Filters a collection based on <paramref name="predicate"/>.
        /// </summary>
        /// <param name="predicate">A function that is used to determine if the item should be returned</param>
        /// <returns>
        /// A collection of filtered items.
        /// </returns>
        public IEnumerable<T> Where(Func<T, bool> predicate)
                    => this.AsEnumerable().Where(predicate);

        /// <summary>
        /// Projects each element of an <see cref="ARecord"/> into a new form.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the value returned by <paramref name="selector"/>.
        /// </typeparam>
        /// <param name="selector">
        /// A transform function to apply to each element.
        /// </param>
        /// <returns>
        /// An IEnumerable&lt;T&gt; whose elements are the result of invoking the transform function on each element of <paramref name="selector"/>.
        /// </returns>
        public IEnumerable<TResult> Select<TResult>(Func<T, TResult> selector)
            => this.AsEnumerable().Select(selector);

        /// <summary>
        /// Returns IEnumerable&gt;<see cref="ARecord"/>&lt; for the records of this set based on <see cref="SetRecords.DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="SetRecords.Operate(dynamic, Operation[])"/>
        /// <seealso cref="SetRecords.DefaultQueryPolicy"/>
        public new IEnumerable<T> AsEnumerable(Client.Exp filterExpression = null)
        {
            var queryPolicy = filterExpression == null
                                    ? this.DefaultQueryPolicy
                                    : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                    .AerospikeClient
                                    .Query(queryPolicy,
                                            string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                            ? new Statement() { Namespace = this.Namespace }
                                            : new Statement() { Namespace = this.Namespace, SetName = this.SetName });

            while (recordset.Next())
            {
                yield return (T)CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        #endregion

        #region Idx Methods
        /// <summary>
        /// Creates a secondary index on this set for a bin <see href="https://docs.aerospike.com/server/guide/query"/>
        /// </summary>
        /// <param name="idxName">The name of the index</param>
        /// <param name="idxOnBin">The bin&apos;s values that will be used to build the index</param>
        /// <param name="indexType">The type of index to be built</param>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        /// <seealso cref="DropIndex(string)"/>
        new public SetRecords<T> CreateIndex(string idxName, string idxOnBin, Client.IndexType indexType)
        {
            base.CreateIndex(idxName, idxOnBin, indexType);
            return this;
        }

        /// <summary>
        /// Creates a secondary index on this set for a bin <see href="https://docs.aerospike.com/server/guide/query"/>
        /// </summary>
        /// <param name="idxName">The name of the index</param>
        /// <param name="idxOnBin">The bin&apos;s values that will be used to build the index</param>
        /// <param name="indexType">The type of index to be built</param>
        /// <param name="indexCollectionType">The bin must be a collection and this determines on to build the index on the collection.</param>
        /// <param name="ctx">Provides additional processing of the collection</param>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="DropIndex(string)"/>
        new public SetRecords<T> CreateIndex(string idxName, string idxOnBin,
                                            Client.IndexType indexType,
                                            Client.IndexCollectionType indexCollectionType, params Client.CTX[] ctx)
        {
            base.CreateIndex(idxName, idxOnBin, indexType, indexCollectionType, ctx);
            return this;
        }

        /// <summary>
        /// Drops a secondary index.
        /// </summary>
        /// <param name="idxName">The name of the index</param>
        /// <returns></returns>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        new public SetRecords<T> DropIndex(string idxName)
        {
            base.DropIndex(idxName);
            return this;
        }
        #endregion
       
        #region IEnumerable

        abstract protected ARecord CreateRecord([NotNull] ANamespaceAccess setAccess,
                                                    [NotNull] Client.Key key,
                                                    [NotNull] Record record,
                                                    string[] binNames,
                                                    int binsHashCode,
                                                    ARecord.DumpTypes recordView = ARecord.DumpTypes.Record);

        public new IEnumerator<T> GetEnumerator()
        {
            using var recordset = this.SetAccess.AerospikeConnection
                                    .AerospikeClient
                                    .Query(this.DefaultQueryPolicy,
                                            string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                            ? new Statement() { Namespace = this.Namespace }
                                            : new Statement() { Namespace = this.Namespace, SetName = this.SetName });

            while (recordset.Next())
            {                    
                yield return (T) CreateRecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                this.BinsHashCode,
                                                recordView: this.DefaultRecordView);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
        #endregion
    }

    /// <summary>
    /// Represents information about an Aerospike set within a namespace. 
    /// It also contains the complete result set of this Aerospike set and is Enumerable returning a collection of <see cref="ARecord"/>s.
    /// </summary>
    [DebuggerDisplay("{ToString()}")]
    public class SetRecords : IEnumerable<ARecord>, IEquatable<ARecord>, IEquatable<SetRecords>
    {
        #region Constructors
        public SetRecords([NotNull] LPSet lpSet,
                            [NotNull] ANamespaceAccess setAccess, 
                            [NotNull] string setName,
                            params string[] bins)
            : this(setAccess, setName, bins) 
        {
            this.LPset = lpSet;            
        }

        public SetRecords([NotNull] ANamespaceAccess setAccess,
                            [NotNull] string setName,
                            params string[] bins)
        {            
            this.SetName = setName;
            this.SetAccess = setAccess;
            this.SetFullName = $"{this.Namespace}.{this.SetName}";
            this._bins = Helpers.RemoveDups(bins);

            this.DefaultWritePolicy = new WritePolicy(this.SetAccess.DefaultWritePolicy);
            this.DefaultReadPolicy = new Policy(this.SetAccess.DefaultReadPolicy);
            this.DefaultQueryPolicy = new QueryPolicy(this.SetAccess.DefaultQueryPolicy);
            this.DefaultRecordView = this.SetAccess.AerospikeConnection.RecordView;

        }

        public SetRecords([NotNull] SetRecords clone)
        {
            this.LPset = clone.LPset;
            this.SetName = clone.SetName;
            this.SetAccess = clone.SetAccess;
            this._bins = clone._bins;
            this._binsHashCode= clone._binsHashCode;

            this.DefaultWritePolicy = new WritePolicy(clone.DefaultWritePolicy);
            this.DefaultReadPolicy = new Policy(clone.DefaultReadPolicy);
            this.DefaultQueryPolicy = new QueryPolicy(clone.DefaultQueryPolicy);
            this.DefaultRecordView = clone.DefaultRecordView;
        }
        #endregion

        #region Settings, Record State, etc.

        public LPSet LPset { get; }

        internal bool TryAddBin(string binName, Type dataType, bool updateNamespace)
        {
            lock (this)
            {
                var added = this.LPset?.AddBin(binName, dataType ?? typeof(AValue)) ?? false;

                if (updateNamespace)
                    added = this.SetAccess.TryAddBin(binName) || added;

                if (this._bins.Length == 0)
                {
                    if (this.BinNames.Contains(binName)) return added;
                    this._bins = this.SetAccess.BinNames;
                }

                this._bins = this._bins.Append(binName).ToArray();
                this._binsHashCode = 0;

                return true;
            }
        }

        internal bool TryRemoveBin(string removeBinName, bool updateNamespace)
        {
            lock (this)
            {
                var removed = this.LPset?.RemoveBin(removeBinName) ?? false;

                if (updateNamespace)
                    removed = this.SetAccess.TryRemoveBin(removeBinName) || removed;

                if (this._bins.Length == 0 || !this._bins.Any(n => n == removeBinName))
                    return false;

                this._bins = this._bins
                                .Where(n => n != removeBinName)
                                .ToArray();
                this._binsHashCode = 0;

                return true;
            }
        }

        /// <summary>
        /// Sets how records are displayed using the LinqPad <see cref="LINQPad.Extensions.Dump{T}(T)"/> method.
        /// See <see cref="ARecord.DumpTypes"/> for more information.
        /// </summary>
        /// <seealso cref="ARecord.DumpTypes"/>
        /// <seealso cref="ChangeRecordView(ARecord.DumpTypes)"/>
        public ARecord.DumpTypes DefaultRecordView { get; set; }
        
        /// <summary>
        /// Changes how records are displayed using the LinqPad <see cref="LINQPad.Extensions.Dump{T}(T)"/> method.        
        /// </summary>
        /// <param name="newRecordView">See <see cref="ARecord.DumpTypes"/> for more information.</param>
        /// <returns>This instance</returns>
        /// <seealso cref="ARecord.DumpTypes"/>
        /// <seealso cref="DefaultRecordView"/>
        public SetRecords ChangeRecordView(ARecord.DumpTypes newRecordView)
        {
            this.DefaultRecordView = newRecordView;
            return this;
        }

        public ANamespaceAccess SetAccess { get; }

        private int _binsHashCode = 0;

        /// <summary>
        /// Returns the hash Code for the defined bins for this Set&apos;s records. 
        /// </summary>
        public int BinsHashCode
        {
            get
            {
                if (this._binsHashCode != 0)
                    return this._binsHashCode;

                if (this._bins.Length == 0)
                {
                    return this._binsHashCode = Helpers.GetStableHashCode(this.BinNames);
                }

                return this._binsHashCode = Helpers.GetStableHashCode(this._bins);
            }
        }

        #endregion

        #region Aerospike Client Properties, Policies, Put, Get, Query, etc.
        /// <summary>
        /// Returns the Aerospike &quot;Namespace&apos;s&quot; name
        /// </summary>
        public string Namespace { get { return this.SetAccess.Namespace; } }

        /// <summary>
        /// Returns the Aerospike &quot;Set&apos;s&quot; name
        /// </summary>
        public string SetName { get; }

        /// <summary>
        /// Returns the Set&apos;s name prefixed with the namespace.
        /// </summary>
        public string SetFullName { get; }

        /// <summary>
        /// The default write policy used for writing.
        /// <see href="https://docs.aerospike.com/apidocs/csharp/html/t_aerospike_client_writepolicy"/>
        /// </summary>
        public WritePolicy DefaultWritePolicy { get; set; }

        /// <summary>
        /// <see href="https://docs.aerospike.com/apidocs/csharp/html/t_aerospike_client_policy"/>
        /// </summary>
        public Policy DefaultReadPolicy { get; set; }
        /// <summary>
        /// <see href="https://docs.aerospike.com/apidocs/csharp/html/t_aerospike_client_querypolicy"/>
        /// </summary>
        public QueryPolicy DefaultQueryPolicy { get; set; }

        protected string[] _bins = Array.Empty<string>();
        /// <summary>
        /// Returns all the bin names possible for this set.
        /// </summary>
        public string[] BinNames { get => this._bins; }
        
        /// <summary>
        /// Determines the Expiration in seconds of a record TTL
        /// </summary>
        /// <param name="ttl"></param>
        /// <returns></returns>
        public static int DetermineExpiration(TimeSpan ttl)
        {
            return (int)ttl.TotalSeconds;
        }

        /// <summary>
        /// Determines the Expiration based on when the record should be expired.
        /// </summary>
        /// <param name="expirationDate"></param>
        /// <returns>Expiration of a record in seconds</returns>
        public static int DetermineExpiration(DateTimeOffset expirationDate)
        {
            return (int)expirationDate.Subtract(DateTimeOffset.UtcNow).TotalSeconds;
        }

        /// <summary>
        /// Determines the TTL based on an expiration date.
        /// </summary>
        /// <param name="expirationDate"></param>
        /// <returns></returns>
        public static TimeSpan DetermineTTL(DateTimeOffset expirationDate)
        {
            return expirationDate.Subtract(DateTimeOffset.UtcNow);
        }

        #region Put Methods
        /// <summary>
        /// Puts (Writes) a DB record based on the provided record including Expiration.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>        
        /// <param name="record">
        /// A <see cref="ARecord"/> object used to add or update the associated record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">
        /// Time-to-live of the record. 
        /// If null (default), the TTL of <paramref name="record"/> is used.
        /// </param>
        public void Put([NotNull] ARecord record,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, 
                                    record.Aerospike.Key, 
                                    record.Aerospike.GetValues(), 
                                    writePolicy: writePolicy, 
                                    ttl: ttl ?? record.Aerospike.TTL);        

        /// <summary>
        /// Puts (Writes) a DB record based on the provided key and bin values.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="binValues">
        /// A dictionary where the key is the bin and the value is the bin&apos;s value.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>
        public void Put<V>([NotNull] dynamic primaryKey,
                            [NotNull] IDictionary<string, V> binValues,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, binValues, 
                                     writePolicy: writePolicy, ttl: ttl);        

        /// <summary>
        /// Puts (writes) a bin to the DB record.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="bin">BinName Name</param>
        /// <param name="binValue">
        /// BinName&apos;s Value.
        /// If null, the bin is removed from the record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>
        public void Put([NotNull] dynamic primaryKey,
                            [NotNull] string bin,
                            [NotNull] object binValue,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, bin, binValue, writePolicy: writePolicy, ttl: ttl);

        /// <summary>
        /// Puts (writes) a bin to the DB record.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="bin">BinName Name</param>
        /// <param name="binValue">
        /// BinName&apos;s Value.
        /// If null, the bin is removed from the record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>        
        public void Put([NotNull] dynamic primaryKey,
                            [NotNull] string bin,
                            [NotNull] string binValue,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, bin, binValue, writePolicy: writePolicy, ttl: ttl);


        /// <summary>
        /// Puts (writes) a bin to the DB record.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="bin">BinName Name</param>
        /// <param name="listValue">
        /// BinName&apos;s Value.
        /// If null, the bin is removed from the record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>        
        public void Put<T>([NotNull] dynamic primaryKey,
                            [NotNull] string bin,
                            [NotNull] IList<T> listValue,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, bin, listValue, writePolicy: writePolicy, ttl: ttl);

        /// <summary>
        /// Puts (writes) a bin to the DB record.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="bin">BinName Name</param>
        /// <param name="collectionValue">
        /// BinName&apos;s Value.
        /// If null, the bin is removed from the record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>        
        public void Put<K,V>([NotNull] dynamic primaryKey,
                                [NotNull] string bin,
                                [NotNull] IDictionary<K,V> collectionValue,
                                WritePolicy writePolicy = null,
                                TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, bin, collectionValue, writePolicy: writePolicy, ttl: ttl);


        /// <summary>
        /// Puts (writes) a bin to the DB record.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="bin">BinName Name</param>
        /// <param name="collectionValue">
        /// BinName&apos;s Value.
        /// If null, the bin is removed from the record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>        
        public void Put<T>([NotNull] dynamic primaryKey,
                            [NotNull] string bin,
                            [NotNull] IEnumerable<T> collectionValue,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, bin, collectionValue, writePolicy: writePolicy, ttl: ttl);        

        /// <summary>
        /// Put (Writes) a DB record based on the provided key and bin values.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="binsToWrite">
        /// A collection of <see cref="Bin"/> objects used to add/update the associated record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>
        public void Put([NotNull] dynamic primaryKey,
                            [NotNull] IEnumerable<Bin> binsToWrite,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null)
            => this.SetAccess.Put(this.SetName, primaryKey, binsToWrite, writePolicy: writePolicy, ttl: ttl);
        
        #endregion

        /// <summary>
        /// Writes the instance where each field/property is a bin name and the associated value the bin's value.
        /// <see cref="Aerospike.Client.BinNameAttribute"/> which allows you to use this name instead of the property/field name.
        /// <see cref="Aerospike.Client.BinIgnoreAttribute"/> which will ignore the property/field name (not written to the DB)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="primaryKey"></param>
        /// <param name="instance"></param>
        /// <param name="transform">
        /// A action that is called to perform customized transformation. 
        /// First argument -- the name of the property/field
        /// Second argument -- the name of the bin (can be different from property/field name, if <see cref="Aerospike.Client.BinNameAttribute"/> is defined)
        /// Third argument -- the instance being transformed
        /// Fourth argument -- if true the instance is within another object.
        /// Returns the new transformed object or null to indicate that this bin should be skipped.
        /// </param>
        /// <param name="doctumentBinName">
        /// If provided the record is created as a document and this will be the name of the bin. 
        /// </param>
        /// <param name="writePolicy"></param>
        /// <param name="ttl"></param>
        /// <seealso cref="Aerospike.Client.BinNameAttribute"/>
        /// <seealso cref="Aerospike.Client.BinIgnoreAttribute"/>
        public void WriteObject<T>([NotNull] dynamic primaryKey,
                                    [NotNull] T instance,
                                    Func<string, string, object, bool, object> transform = null,
                                    string doctumentBinName = null,
                                    WritePolicy writePolicy = null,
                                    TimeSpan? ttl = null)
            => this.SetAccess.WriteObject<T>(this.SetName, primaryKey, instance, 
                                                transform: transform, doctumentBinName: doctumentBinName, writePolicy: writePolicy, ttl: ttl);        

        #region Delete/Trunc Methods
        /// <summary>
        /// Deletes a DB record based on the provided record.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>        
        /// <param name="record">
        /// A <see cref="ARecord"/> object used to add or update the associated record.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If noy provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <returns>
        /// Returns true, if the DB record is deleted.
        /// </returns>
        public bool Delete([NotNull] ARecord record,
                            WritePolicy writePolicy = null)
            => this.Delete(record.Aerospike.Key, writePolicy: writePolicy ?? this.DefaultWritePolicy);        

        /// <summary>
        /// Deletes the DB record associated with the primary key.
        /// Note that if the namespace and/or set is different, this instances&apos;s values are used.
        /// </summary>
        /// <param name="primaryKey">
        /// Primary AerospikeKey.
        /// This can be a <see cref="Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <returns>
        /// Returns true, if the DB record is deleted.
        /// </returns>        
        public bool Delete([NotNull] dynamic primaryKey, WritePolicy writePolicy = null)
        {
            var writePolicyDelete = writePolicy ?? this.DefaultWritePolicy;
            
            return this.SetAccess
                            .AerospikeConnection
                            .AerospikeClient.Delete(writePolicyDelete,
                                                    Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName));
        }

        /// <summary>
        /// Truncates the Set
        /// </summary>
        /// <param name="infoPolicy">
        /// The <see cref="InfoPolicy"/> used for the truncate. If not provided, the default is used.
        /// </param>
        /// <param name="before">
        /// A Date/time used to truncate the set. Records before this time will be truncated. 
        /// The default is everything up to when this was executed (DateTime.Now).
        /// </param>
        /// <seealso cref="ANamespaceAccess.Truncate(InfoPolicy, DateTime?)"/>
        /// <exception cref="InvalidOperationException">Thrown if the cluster is a production cluster. Can disable this by going into the connection properties.</exception>
        public void Truncate(InfoPolicy infoPolicy = null, DateTime? before = null)
        {
            if (this.SetAccess.AerospikeConnection.CXInfo.IsProduction)
                throw new InvalidOperationException("Cannot Truncate a Cluster marked \"In Production\"");

            this.SetAccess
                    .AerospikeConnection
                    .AerospikeClient.Truncate(infoPolicy, this.Namespace, this.SetName, before ?? DateTime.Now);
        }
        #endregion

        #region Get Methods
        /// <summary>
        /// Returns the record based on the primary key
        /// </summary>
        /// <param name="primaryKey">
        /// The primary key can be a <see cref="Aerospike.Client.Key"/>, <see cref="Aerospike.Client.Value"/>, digest (byte[]), or a .net type.
        /// </param>
        /// <param name="bins">
        /// An optional arguments, if provided only those bins are returned.
        /// </param>
        /// <returns>
        /// A record if the primary key is found otherwise null.
        /// </returns>        
        /// <seealso cref="Get(dynamic, Expression, string[])"/>
        public ARecord Get([NotNull] dynamic primaryKey, params string[] bins)
        {            
            var key = Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName);

            var record = this.SetAccess
                                .AerospikeConnection
                                .AerospikeClient
                                .Get(this.DefaultReadPolicy, key, bins.Length == 0 ? null : bins);

            if (record == null) return null;

            return new ARecord(this.SetAccess,
                                    key,
                                    record,
                                    this._bins,
                                    dumpType: this.DefaultRecordView);
        }

        /// <summary>
        /// Returns the record based on the primary key
        /// </summary>
        /// <param name="primaryKey">
        /// The primary key can be a <see cref="Aerospike.Client.Key"/>, <see cref="Aerospike.Client.Value"/>, digest (byte[]), or a .net type.
        /// </param>
        /// <param name="filterExpresion">
        /// A filter <see cref="Aerospike.Client.Expression"/> that is applied after obtaining the record via the primary key.
        /// </param>
        /// <param name="bins">
        /// An optional arguments, if provided only those bins are returned.
        /// </param>
        /// <returns>
        /// A record if the primary key is found otherwise null.
        /// </returns>
        /// <seealso cref="Get(dynamic, string[])"/>        
        /// <seealso cref="Query(Exp, string[])"/>
        public ARecord Get([NotNull] dynamic primaryKey, Expression filterExpresion, params string[] bins)
        {
            var key = Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName);

            var policy = new Client.Policy(this.DefaultReadPolicy) {  filterExp = filterExpresion };

            var record = this.SetAccess
                                .AerospikeConnection
                                .AerospikeClient
                                .Get(policy, key, bins.Length == 0 ? null : bins);

            if (record == null) return null;

            return new ARecord(this.SetAccess,
                                    key,
                                    record,
                                    this._bins,
                                    dumpType: this.DefaultRecordView);
        }
        #endregion

        #region Query Methods

        /// <summary>
        /// Returns all the records based on the provided bins.
        /// </summary>
        /// <param name="bins">Only include these bins in the result.</param>
        /// <returns>
        /// A collection of records defined by <paramref name="bins"/>
        /// </returns>
        /// <exception cref="AerospikeException">Thrown if an index cannot be found to match the filter</exception>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="Query(string[])"/>
        public IEnumerable<ARecord> Query(params string[] bins)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy);
            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return new ARecord(this.SetAccess,
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Performs a <see cref="Client.AerospikeClient.Query(QueryPolicy, Statement)"/> applying the expression filter.
        /// </summary>
        /// <param name="filterExpression">
        /// The Aerospike filter <see cref="Client.Exp"/> that will be applied.
        /// <seealso cref="Aerospike.Client.ListExp"/>
        /// <seealso cref="Aerospike.Client.MapExp"/>
        /// <seealso cref="Aerospike.Client.BitExp"/>
        /// <seealso cref="Aerospike.Client.HLLExp"/>
        /// </param>
        /// <param name="bins">Return only the provided bins in the result set</param>
        /// <returns>
        /// The result set based on the expression filter.
        /// </returns>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="Query(string[])"/>        
        /// <seealso cref="Operate(dynamic, Operation[])"/>
        public IEnumerable<ARecord> Query([NotNull] Client.Exp filterExpression, params string[] bins)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy,
                                           string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                                   ? new Statement() { Namespace = this.Namespace, BinNames = bins }
                                                   : new Statement() { Namespace = this.Namespace, SetName = this.SetName, BinNames = bins });

            while (recordset.Next())
            {
                yield return new ARecord(this.SetAccess,
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Performs a secondary index query using the provided <see cref="Client.Filter"/>.
        /// See <see cref="ASecondaryIndexAccess"/> for directly using secondary indexes.
        /// </summary>
        /// <param name="secondaryIdxFilter">The filter used against the secondary index</param>
        /// <param name="bins">Only include these bins in the result.</param>
        /// <returns>
        /// A collection of records that match the filter.
        /// </returns>
        /// <exception cref="AerospikeException">Thrown if an index cannot be found to match the filter</exception>
        /// <seealso cref="Query(Filter, Exp, string[])"/>     
        /// <seealso cref="Query(Exp, string[])"/>
        /// <seealso cref="Query(string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        /// <seealso cref="ASecondaryIndexAccess.Query(Filter, Exp, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.Query(long, long, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.Query(dynamic, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.GetFilter(object, CTX[])"/>
        /// <seealso cref="ASecondaryIndexAccess.GetFilter(long, long, CTX[])"/>
        public IEnumerable<ARecord> Query([NotNull] Client.Filter secondaryIdxFilter, params string[] bins)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy);
            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            stmt.SetFilter(secondaryIdxFilter);
            stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return new ARecord(this.SetAccess,
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }
        
        /// <summary>
        /// Performs a secondary index query using the provided <see cref="Client.Filter"/> and than apply the filter expression.
        /// See <see cref="ASecondaryIndexAccess"/> for directly using secondary indexes.
        /// </summary>
        /// <param name="secondaryIdxFilter">The filter used against the secondary index</param>
        /// <param name="filterExpression">
        /// The Aerospike filter <see cref="Client.Exp"/> that will be applied after the index filter is applied.
        /// <seealso cref="Aerospike.Client.ListExp"/>
        /// <seealso cref="Aerospike.Client.MapExp"/>
        /// <seealso cref="Aerospike.Client.BitExp"/>
        /// <seealso cref="Aerospike.Client.HLLExp"/>
        /// </param>
        /// <param name="bins">Only include these bins in the result.</param>
        /// <returns>
        /// A collection of records that match the filters.
        /// </returns>
        /// <exception cref="AerospikeException">Thrown if an index cannot be found to match the filter</exception>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="Query(string[])"/>
        /// <seealso cref="Query(Exp, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.Query(Filter, Exp, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.Query(long, long, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.Query(dynamic, string[])"/>
        /// <seealso cref="ASecondaryIndexAccess.GetFilter(object, CTX[])"/>
        /// <seealso cref="ASecondaryIndexAccess.GetFilter(long, long, CTX[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        public IEnumerable<ARecord> Query([NotNull] Client.Filter secondaryIdxFilter, [NotNull] Client.Exp filterExpression, params string[] bins)
        {            
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            stmt.SetFilter(secondaryIdxFilter);
            stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return new ARecord(this.SetAccess,
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }
        
        /// <summary>
        /// Performs a secondary index query or expression query using the provided arguments.
        /// </summary>
        /// <param name="idxName">The name of the index. Can be null.</param>
        /// <param name="secondaryIdxFilter">The filter used to obtain the result of an index query. Can be null.</param>
        /// <param name="filterExpression">The expression that will be applied to the result set. Can be null.</param>
        /// <param name="bins">The bins that will be returned from the result set. Can be null.</param>
        /// <returns>
        /// The result set of the query. 
        /// </returns>
        /// <remarks>To just provide the <paramref name="idxName"/> you must explicitly provide the argument name &quot;idxName:&quot;, otherwise it will be treated as a bin name.</remarks>
        public IEnumerable<ARecord> Query([AllowNull] string idxName = null,
                                            [AllowNull] Client.Filter secondaryIdxFilter = null,
                                            [AllowNull] Client.Exp filterExpression = null,
                                            [AllowNull] string[] bins = null)
        {
            var queryPolicy = new QueryPolicy(this.DefaultQueryPolicy) 
                                                { filterExp = filterExpression == null
                                                                ? null
                                                                : Exp.Build(filterExpression) };

            var stmt = new Statement();

            stmt.SetNamespace(this.Namespace);

            if (!string.IsNullOrEmpty(this.SetName) && this.SetName != LPSet.NullSetName)
                stmt.SetSetName(this.SetName);

            if(secondaryIdxFilter != null)
                stmt.SetFilter(secondaryIdxFilter);
            if(!string.IsNullOrEmpty(idxName))
                stmt.SetIndexName(idxName);
            if(bins != null)
                stmt.SetBinNames(bins);

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy, stmt);

            while (recordset.Next())
            {
                yield return new ARecord(this.SetAccess,
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }

        #endregion

        #region Operate Methods
        /// <summary>
        /// Executes an Aerospike operation against the set based on the primary key.
        /// </summary>
        /// <param name="primaryKey">
        /// The primary key which can be a Aerospike AerospikeKey or Value or a .Net type. 
        /// </param>
        /// <param name="operations">
        /// Aerospike operations (Expression). <see cref="Aerospike.Client.Operation"/>, <see cref="Aerospike.Client.ExpOperation"/>, <see cref="Aerospike.Client.Operation"/>, <see cref="Aerospike.Client.MapOperation"/>, or <see cref="Aerospike.Client.ListOperation"/>
        /// </param>
        /// <returns>
        /// The resulting record or an exception... 
        /// </returns>       
        public ARecord Operate([NotNull] dynamic primaryKey, params Operation[] operations)
        {
            var key = Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName);

            var record = this.SetAccess
                                .AerospikeConnection
                                .AerospikeClient
                                .Operate(this.DefaultWritePolicy, key, operations);

            return new ARecord(this.SetAccess,
                                    key,
                                    record,
                                    null,
                                    dumpType: this.DefaultRecordView);
        }

        #endregion

        #region Idx Methods
        /// <summary>
        /// Creates a secondary index on this set for a bin <see href="https://docs.aerospike.com/server/guide/query"/>
        /// </summary>
        /// <param name="idxName">The name of the index</param>
        /// <param name="idxOnBin">The bin&apos;s values that will be used to build the index</param>
        /// <param name="indexType">The type of index to be built</param>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        /// <seealso cref="DropIndex(string)"/>
        public SetRecords CreateIndex(string idxName, string idxOnBin, Client.IndexType indexType)
        {
            var policy = new Policy();
            var task = this.SetAccess.AerospikeConnection.AerospikeClient
                                .CreateIndex(policy, this.Namespace, this.SetName, idxName, idxOnBin, indexType);

            task.Wait();

            DynamicDriver._Connection.CXInfo.ForceRefresh();

            return this;
        }

        /// <summary>
        /// Creates a secondary index on this set for a bin <see href="https://docs.aerospike.com/server/guide/query"/>
        /// </summary>
        /// <param name="idxName">The name of the index</param>
        /// <param name="idxOnBin">The bin&apos;s values that will be used to build the index</param>
        /// <param name="indexType">The type of index to be built</param>
        /// <param name="indexCollectionType">The bin must be a collection and this determines on to build the index on the collection.</param>
        /// <param name="ctx">Provides additional processing of the collection</param>
        /// <seealso cref="Query(Filter, Exp, string[])"/>
        /// <seealso cref="Query(Filter, string[])"/>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="DropIndex(string)"/>
        public SetRecords CreateIndex(string idxName, string idxOnBin,
                                            Client.IndexType indexType,
                                            Client.IndexCollectionType indexCollectionType, params Client.CTX[] ctx)
        {
            var policy = new Policy();
            var task = this.SetAccess.AerospikeConnection.AerospikeClient
                                .CreateIndex(policy, this.Namespace, this.SetName,
                                                idxName, idxOnBin, indexType, indexCollectionType, ctx);

            task.Wait();

            DynamicDriver._Connection.CXInfo.ForceRefresh();

            return this;
        }

        /// <summary>
        /// Drops a secondary index.
        /// </summary>
        /// <param name="idxName">The name of the index</param>
        /// <returns></returns>
        /// <seealso cref="CreateIndex(string, string, IndexType)"/>
        /// <seealso cref="CreateIndex(string, string, IndexType, IndexCollectionType, CTX[])"/>
        public SetRecords DropIndex(string idxName)
        {
            var policy = new Policy();

            var task = this.SetAccess.AerospikeConnection.AerospikeClient.DropIndex(policy, this.Namespace, this.SetName, idxName);
            task.Wait();

            DynamicDriver._Connection.CXInfo.ForceRefresh();

            return this;
        }
        #endregion
       
        #endregion

        #region Linq Type Methods

        public SetRecords Clone() => new SetRecords(this);

        /// <summary>
        /// Returns the top number of records from the set based on <see cref="DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="numberRecords">Number of records to return</param>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns>A collection of records or empty set</returns>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="Operate(dynamic, Operation[])"/>
        /// <seealso cref="DefaultQueryPolicy"/>
        public IEnumerable<ARecord> Take(int numberRecords, Client.Exp filterExpression = null)
        {
            if (numberRecords <= 0) yield break;

            var queryPolicy = filterExpression == null
                                    ? this.DefaultQueryPolicy
                                    : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy,
                                           string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                           ? new Statement() { Namespace = this.Namespace, MaxRecords = numberRecords }
                                           : new Statement() { Namespace = this.Namespace, SetName = this.SetName, MaxRecords = numberRecords });

            while (recordset.Next())
            {
                yield return new ARecord(this.SetAccess, 
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }

        /// <summary>
        /// Returns the first record from the set based on <see cref="DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns></returns>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="Operate(dynamic, Operation[])"/>
        /// <seealso cref="DefaultQueryPolicy"/>
        public ARecord First(Client.Exp filterExpression = null)
        {
            return this.Take(1, filterExpression).First();
        }

        /// <summary>
        /// Returns the first record or null from the set based on <see cref="DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns></returns>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="Operate(dynamic, Operation[])"/>
        /// <seealso cref="DefaultQueryPolicy"/>
        public ARecord FirstOrDefault(Client.Exp filterExpression = null)
        {
            return this.Take(1, filterExpression).FirstOrDefault();
        }

        /// <summary>
        /// Skips the number of records from the set based on <see cref="DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="numberRecords">Number of records to skip</param>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <returns></returns>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="AsEnumerable(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="Operate(dynamic, Operation[])"/>
        /// <seealso cref="DefaultQueryPolicy"/>
        public IEnumerable<ARecord> Skip(int numberRecords, Client.Exp filterExpression = null)
        {
            int currentIdx = 0;
            var queryPolicy = filterExpression == null
                                    ? this.DefaultQueryPolicy
                                    : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                   .AerospikeClient
                                   .Query(queryPolicy,
                                           string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                           ? new Statement() { Namespace = this.Namespace }
                                           : new Statement() { Namespace = this.Namespace, SetName = this.SetName });
               
                while (recordset.Next())
                {
                    if(++currentIdx > numberRecords)
                        yield return new ARecord(this.SetAccess,
                                                    recordset.Key,
                                                    recordset.Record,
                                                    this._bins,
                                                    setBinsHashCode: this.BinsHashCode,
                                                    dumpType: this.DefaultRecordView);
                }
        }

        /// <summary>
        /// Filters a collection based on <paramref name="predicate"/>.
        /// </summary>
        /// <param name="predicate">A function that is used to determine if the item should be returned</param>
        /// <returns>
        /// A collection of filtered items.
        /// </returns>
        public IEnumerable<ARecord> Where(Func<ARecord,bool> predicate)
                    => this.AsEnumerable().Where(predicate);
        
        /// <summary>
        /// Projects each element of an <see cref="ARecord"/> into a new form.
        /// </summary>
        /// <typeparam name="TResult">
        /// The type of the value returned by <paramref name="selector"/>.
        /// </typeparam>
        /// <param name="selector">
        /// A transform function to apply to each element.
        /// </param>
        /// <returns>
        /// An IEnumerable&lt;T&gt; whose elements are the result of invoking the transform function on each element of <paramref name="selector"/>.
        /// </returns>
        public IEnumerable<TResult> Select<TResult>(Func<ARecord, TResult> selector)
            => this.AsEnumerable().Select(selector);

        /// <summary>
        /// Returns IEnumerable&gt;<see cref="ARecord"/>&lt; for the records of this set based on <see cref="DefaultQueryPolicy"/> or <paramref name="filterExpression"/>.
        /// </summary>
        /// <param name="filterExpression">A Filter <see cref="Client.Exp"/> used to obtain the collection of records.</param>
        /// <seealso cref="Take(int, Client.Exp)"/>
        /// <seealso cref="First(Client.Exp)"/>
        /// <seealso cref="FirstOrDefault(Client.Exp)"/>
        /// <seealso cref="Get(dynamic, string[])"/>
        /// <seealso cref="Operate(dynamic, Operation[])"/>
        /// <seealso cref="DefaultQueryPolicy"/>
        public IEnumerable<ARecord> AsEnumerable(Client.Exp filterExpression = null)
        {
            var queryPolicy = filterExpression == null
                                    ? this.DefaultQueryPolicy
                                    : new QueryPolicy(this.DefaultQueryPolicy) { filterExp = Exp.Build(filterExpression) };

            using var recordset = this.SetAccess.AerospikeConnection
                                    .AerospikeClient
                                    .Query(queryPolicy,
                                            string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                            ? new Statement() { Namespace = this.Namespace }
                                            : new Statement() { Namespace = this.Namespace, SetName = this.SetName });

                while (recordset.Next())
                {
                    yield return new ARecord(this.SetAccess,
                                                recordset.Key,
                                                recordset.Record,
                                                this._bins,
                                                setBinsHashCode: this.BinsHashCode,
                                                dumpType: this.DefaultRecordView);
                }
        }

        /// <summary>
        /// Returns true if primary key exists
        /// </summary>
        /// <param name="primaryKey">
        /// The primary key can be a <see cref="Aerospike.Client.Key"/>, <see cref="Aerospike.Client.Value"/>, digest (byte[]), or a .net type.
        /// </param>  
        /// <param name="filterExpresion">
        /// A filter <see cref="Aerospike.Client.Expression"/> that is applied after obtaining the record via the primary key.
        /// </param>
        /// <returns>
        /// True if the <paramref name="primaryKey"/> exists, otherwise false.
        /// </returns>
        /// <seealso cref="Get(dynamic, Expression, string[])"/>
        /// <seealso cref="Query(Exp, string[])"/>
        public bool Exists([NotNull] dynamic primaryKey, Expression filterExpresion)
        {
            var key = Helpers.DetermineAerospikeKey(primaryKey, this.Namespace, this.SetName);

            var policy = new Client.Policy(this.DefaultReadPolicy) { filterExp = filterExpresion };

            return this.SetAccess
                        .AerospikeConnection
                        .AerospikeClient
                        .Exists(policy, key);
        }


        /// <summary>
        /// Placeholder for <see cref="System.Linq.Enumerable.Count{TSource}(IEnumerable{TSource})"/>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException">Always throw since it must be executed from <see cref="AsEnumerable(Exp)"/></exception>
        public int Count()
        {
            throw new NotImplementedException($"Count must be executed from the \"AsEnumerable\" method. Ex: {this.SetFullName}.AsEnumerable().Count()");
        }

        /// <summary>
        /// Placeholder for <see cref="System.Linq.Enumerable.OrderBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey})"/>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException">Always throw since it must be executed from <see cref="AsEnumerable(Exp)"/></exception>
        public IEnumerable<ARecord> OrderBy(Func<ARecord,int> orderby)            
        {
            throw new NotImplementedException("OrderBy must be executed from the \"AsEnumerable\" method. Ex: mySet.AsEnumerable().OrderBy(r => r.PK)");
        }

        /// <summary>
        /// Placeholder for <see cref="System.Linq.Enumerable.OrderByDescending{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey})"/>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException">Always throw since it must be executed from <see cref="AsEnumerable(Exp)"/></exception>
        public IEnumerable<ARecord> OrderByDescending(Func<ARecord, int> orderby)
        {
            throw new NotImplementedException("OrderByDescending must be executed from the \"AsEnumerable\" method. Ex: mySet.AsEnumerable().OrderByDescending(r => r.PK)");
        }
        #endregion

        #region Export/Import/JSON

        /// <summary>
        /// Exports the records in this set to a JSON file based on <see cref="JsonExportStructure"/>.
        /// </summary>
        /// <param name="exportJSONFile">
        /// The JSON file where the JSON will be written.
        /// If this is an existing directory, the file name will be generated where the name contains the namespace and set names with the JSON extension.
        /// If the file exists it will be overwritten or created.
        /// </param>
        /// <param name="filterExpression">A filter expression that will be applied that will determine the result set.</param>
        /// <param name="indented">If true the JSON string is formatted for readability</param>
        /// <returns>Number of records written</returns>
        /// <seealso cref="ANamespaceAccess.Export(string, Exp, bool)"/>
        /// <seealso cref="Import(string, WritePolicy, TimeSpan?, bool, int, CancellationToken)"/>
        /// <seealso cref="ANamespaceAccess.Import(string, string, WritePolicy, TimeSpan?, bool, int, CancellationToken)"/>
        /// <seealso cref="ANamespaceAccess.Import(string, WritePolicy, TimeSpan?, bool, int, CancellationToken)"/>
        /// <seealso cref="ARecord.Export(bool, JsonSerializerSettings)"/>
        public int Export([NotNull] string exportJSONFile, Client.Exp filterExpression = null, bool indented = true)
        {
            var jsonStr = new StringBuilder();
            int cnt = 0;
            var jsonSettings = new JsonSerializerSettings
                                {
                                    TypeNameHandling = TypeNameHandling.All,
                                    DateParseHandling = DateParseHandling.DateTimeOffset,
                                    NullValueHandling = NullValueHandling.Ignore
                                };

            jsonStr.AppendLine("[");

            foreach(var rec in this.AsEnumerable(filterExpression))
            {                
                jsonStr.Append(rec.Export(indented, jsonSettings));
                jsonStr.AppendLine(",");

                cnt++;
            }

            jsonStr.AppendLine("]");

            if(Directory.Exists(exportJSONFile))
            {
                exportJSONFile = Path.Combine(exportJSONFile, $"{this.Namespace}.{this.SetName}.json");
            }

            File.WriteAllText(exportJSONFile, jsonStr.ToString());

            return cnt;
        }

        /// <summary>
        /// Imports a <see cref="Export(string, Exp, bool)"/> generated JSON file based on <see cref="JsonExportStructure"/>. 
        /// </summary>
        /// <param name="importJSONFile">The JSON file that will be read</param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">
        /// Time-to-live of the records being imported
        /// Note: This is only used, if <paramref name="useImportRecTTL"/> is false.
        /// <see cref="ARecord.AerospikeAPI.TTL"/>
        /// <see cref="ARecord.AerospikeAPI.Expiration"/>
        /// </param>
        /// <param name="useImportRecTTL">
        /// If true, the TTL of the record at export is used.
        /// Otherwise, <paramref name="ttl"/> is used, if provided.
        /// </param>
        /// <param name="maxDegreeOfParallelism">
        /// The maximum degree of parallelism.
        /// <see cref="ParallelOptions.MaxDegreeOfParallelism"/>
        /// </param>
        /// The <see cref="System.Threading.CancellationToken">CancellationToken</see>
        /// associated with this <see cref="ParallelOptions"/> instance.
        /// <param name="cancellationToken">
        /// </param>
        /// <returns>The number of records imported</returns>
        /// <seealso cref="Export(string, Exp, bool)"/>
        /// <seealso cref="ANamespaceAccess.Import(string, string, WritePolicy, TimeSpan?, bool, int, CancellationToken)"/>
        /// <seealso cref="ANamespaceAccess.Import(string, WritePolicy, TimeSpan?, bool, int, CancellationToken)"/>
        /// <seealso cref="AClusterAccess.Import(string, string, string, WritePolicy, int, CancellationToken)"/>
        /// <exception cref="InvalidOperationException">Thrown if the cluster is a production cluster. Can disable this by going into the connection properties.</exception>
        public int Import([NotNull] string importJSONFile,
                            WritePolicy writePolicy = null,
                            TimeSpan? ttl = null,
                            bool useImportRecTTL = false,
                            int maxDegreeOfParallelism = -1,
                            CancellationToken cancellationToken = default)
        {
            return this.SetAccess.Import(importJSONFile,
                                            this.SetName,
                                            writePolicy ?? this.DefaultWritePolicy,
                                            ttl,
                                            useImportRecTTL,
                                            maxDegreeOfParallelism,
                                            cancellationToken);
        }

        /// <summary>
        /// Creates a Json Array of all records in the set based on the <paramref name="filterExpresion"/>, if provided.
        /// </summary>
        /// <param name="filterExpresion"></param>
        /// <param name="pkPropertyName">
        /// The property name used for the primary key. The default is &apos;_id&apos;.
        /// If the primary key value is not present, the digest is used. In these cases the property value will be a sub property where that name will be &apos;$oid&apos; and the value is a byte string.
        /// If this is null, no PK property is written. 
        /// </param>
        /// <param name="useDigest">
        /// If true, always use the PK digest as the primary key.
        /// If false, use the PK value is present, otherwise use the digest. 
        /// Default is false.
        /// </param>
        /// <returns>Json Array of the records in the set.</returns>
        /// <seealso cref="FromJson(string, dynamic, string, string, WritePolicy, TimeSpan?, bool)"/>
        /// <seealso cref="FromJson(string, dynamic, string, string, WritePolicy, TimeSpan?, bool)"/>
        /// <seealso cref="ARecord.FromJson(string, string, dynamic, string, string, string, ANamespaceAccess)"/>
        /// <seealso cref="ARecord.FromJson(string, string, dynamic, string, string, string, ANamespaceAccess)"/>
        /// <seealso cref="ARecord.ToJson(string, bool)"/>
        /// <seealso cref="Aerospike.Client.Exp"/>
        public JArray ToJson(Exp filterExpresion = null, [AllowNull] string pkPropertyName = "_id", bool useDigest = false)
        {
            var jsonArray = new JArray();

            foreach(var rec in this.AsEnumerable(filterExpresion))
            {
                jsonArray.Add(rec.ToJson(pkPropertyName, useDigest));
            }

            return jsonArray;
        }

        /// <summary>
        /// Converts a Json string into an <see cref="ARecord"/> which is than put into this set.
        /// Each top-level property in the Json is translated into a bin and value. Json Arrays and embedded objects are transformed into an Aerospike List or Map&lt;string,object&gt;.
        /// Note: If the Json string is an Json Array, each element is treated as a separate record. 
        ///         If the Json string is a Json Object, the following behavior occurs:
        ///             If <paramref name="jsonBinName"/> is provided, the Json object is treated as an Aerospike document which will be associated with that bin.
        ///             if <paramref name="jsonBinName"/> is null, each json property in that Json object is treated as a separate bin/value.
        ///         You can also insert individual records by calling <see cref="FromJson(string, dynamic, string, string, WritePolicy, TimeSpan?, bool)"/>.
        /// </summary>
        /// <param name="json">
        /// The Json string. 
        /// note: in-line json types are supported.
        ///     Example:
        ///         <code>&quot;bucket_start_date&quot;: &quot;$date&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        /// </param>
        /// <param name="pkPropertyName">
        /// The property name used for the primary key. The default is &apos;_id&apos;.
        /// If the primary key value is not present, the digest is used. In these cases the property value will be a sub property where that name will be &apos;$oid&apos; and the value is a byte string.
        /// </param>
        /// <param name="writePKPropertyName">
        /// If true, the <paramref name="pkPropertyName"/>, is written to the record.
        /// If false (default), it will not be written to the set (only used to define the PK).
        /// </param>
        /// <param name="jsonBinName">
        /// If provided, the Json object is placed into this bin.
        /// If null (default), the each top level Json property will be associated with a bin. Note, if the property name is greater than the bin name limit, an Aerospike exception will occur during the put.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>
        /// <returns>The number of items put.</returns>
        /// <seealso cref="ToJson(Exp, string, bool)"/>
        /// <seealso cref="ARecord.ToJson(string, bool)"/>
        /// <seealso cref="FromJson(string, dynamic, string, string, WritePolicy, TimeSpan?, bool)"/>
        /// <seealso cref="ARecord.FromJson(string, string, dynamic, string, string, string, ANamespaceAccess)"/>
        /// <seealso cref="ARecord.FromJson(string, string, string, string, string, ANamespaceAccess, bool)"/>
        /// <seealso cref="ANamespaceAccess.FromJson(string, string, string, string, WritePolicy, TimeSpan?, bool)"/>
        /// <seealso cref="Put(ARecord, WritePolicy, TimeSpan?)"/>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if the <paramref name="pkPropertyName"/> is not found as a top-level field. 
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Thrown if an unexpected data type is encountered.
        /// </exception>
        /// <remarks>
        /// The Json string can include Json in-line types. Below are the supported types:
        ///     <code>$date</code> or <code>$datetime</code>,
        ///         This can include an optional sub Json Type.Example:
        ///             <code>&quot;bucket_start_date&quot;: &quot;$date&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        ///     <code>$datetimeoffset</code>,
        ///         This can include an optional sub Json Type. Example:
        ///             <code>&quot;bucket_start_datetimeoffset&quot;: &quot;$datetimeoffset&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        ///     <code>$timespan</code>,
        ///         This can include an optional sub Json Type. Example:
        ///             <code>&quot;bucket_start_time&quot;: &quot;$timespan&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        ///     <code>$timestamp</code>,
        ///     <code>$guid</code> or <code>$uuid</code>,
        ///     <code>$oid</code>,
        ///         If the Json string value equals 40 in length it will be treated as a digest and converted into a byte array.
        ///         Example:
        ///             <code>&quot;_id&quot;: { &quot;$oid &quot;: &quot;0080a245fabe57999707dc41ced60edc4ac7ac40&quot; }</code> ==&gt; <code>&quot;_id&quot;:[00 80 A2 45 FA BE 57 99 97 07 DC 41 CE D6 0E DC 4A C7 AC 40]</code>
        ///     <code>$numberint64</code> or <code>$numberlong</code>,
        ///     <code>$numberint32</code>, or <code>$numberint</code>,
        ///     <code>$numberdecimal</code>,
        ///     <code>$numberdouble</code>,
        ///     <code>$numberfloat</code> or <code>$single</code>,
        ///     <code>$numberint16</code> or <code>$numbershort</code>,
        ///     <code>$numberuint32</code> or <code>$numberuint</code>,
        ///     <code>$numberuint64</code> or <code>$numberulong</code>,
        ///     <code>$numberuint16</code> or <code>$numberushort</code>,
        ///     <code>$bool</code> or <code>$boolean</code>;
        /// </remarks>
        public int FromJson(string json, 
                                string pkPropertyName = "_id",
                                string jsonBinName = null,
                                WritePolicy writePolicy = null,
                                TimeSpan? ttl = null,
                                bool writePKPropertyName = false)
        {
            return this.SetAccess.FromJson(this.SetName,
                                            json,
                                            pkPropertyName: pkPropertyName,
                                            jsonBinName: jsonBinName,
                                            writePolicy: writePolicy,
                                            ttl: ttl,
                                            writePKPropertyName: writePKPropertyName);
        }

        /// <summary>
        /// Converts a Json string into an <see cref="ARecord"/> which is than put into this set.
        /// Each top-level property in the Json is translated into a bin and value. Json Arrays and embedded objects are transformed into an Aerospike List or Map&lt;string,object&gt;.
        /// 
        /// Note: If <paramref name="jsonBinName"/> is provided the Json item will completely be placed into this bin as its' value.
        /// </summary>
        /// <param name="json">
        /// The Json string. 
        /// note: in-line json types are supported.
        ///     Example:
        ///         <code>&quot;bucket_start_date&quot;: &quot;$date&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        /// </param>
        /// <param name="primaryKey">
        /// Primary AerospikeKey, if provided. If null, the Json object will have to provide a PK based on <paramref name="jsonBinName"/>.
        /// This can be a <see cref="Client.Key"/>, <see cref="Value"/>, or <see cref="Bin"/> object besides a native, collection, etc. value/object.
        /// </param>
        /// <param name="pkPropertyName">
        /// The property name used for the primary key only if <paramref name="primaryKey"/> is null. The default is &apos;_id&apos;.
        /// </param> 
        /// <param name="writePKPropertyName">
        /// If true, the <paramref name="pkPropertyName"/>, is written to the record.
        /// If false (default), it will not be written to the set (only used to define the PK).
        /// </param>
        /// <param name="jsonBinName">
        /// If provided, the Json object is placed into this bin.
        /// If null (default), the each top level Json property will be associated with a bin. Note, if the property name is greater than the bin name limit, an Aerospike exception will occur during the put.
        /// </param>
        /// <param name="writePolicy">
        /// The write policy. If not provided , the default policy is used.
        /// <seealso cref="WritePolicy"/>
        /// </param>
        /// <param name="ttl">Time-to-live of the record</param>
        /// <returns>The number of items put.</returns>
        /// <seealso cref="ToJson(Exp, string, bool)"/>
        /// <seealso cref="ARecord.ToJson(string, bool)"/>
        /// <seealso cref="FromJson(string, string, string, WritePolicy, TimeSpan?, bool)"/>
        /// <seealso cref="ARecord.FromJson(string, string, dynamic, string, string, string, ANamespaceAccess)"/>
        /// <seealso cref="ARecord.FromJson(string, string, string, string, string, ANamespaceAccess, bool)"/>
        /// <seealso cref="ANamespaceAccess.FromJson(string, string, dynamic, string, string, WritePolicy, TimeSpan?, bool)"/>
        /// <seealso cref="Put(ARecord, WritePolicy, TimeSpan?)"/>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if the <paramref name="pkPropertyName"/> is not found as a top-level field. 
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Thrown if an unexpected data type is encountered.
        /// </exception>
        /// <remarks>
        /// The Json string can include Json in-line types. Below are the supported types:
        ///     <code>$date</code> or <code>$datetime</code>,
        ///         This can include an optional sub Json Type.Example:
        ///             <code>&quot;bucket_start_date&quot;: &quot;$date&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        ///     <code>$datetimeoffset</code>,
        ///         This can include an optional sub Json Type. Example:
        ///             <code>&quot;bucket_start_datetimeoffset&quot;: &quot;$datetimeoffset&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        ///     <code>$timespan</code>,
        ///         This can include an optional sub Json Type. Example:
        ///             <code>&quot;bucket_start_time&quot;: &quot;$timespan&quot;: { &quot;$numberLong&quot;: &quot;1545886800000&quot;}}</code>
        ///     <code>$timestamp</code>,
        ///     <code>$guid</code> or <code>$uuid</code>,
        ///     <code>$oid</code>,
        ///         If the Json string value equals 40 in length it will be treated as a digest and converted into a byte array.
        ///         Example:
        ///             <code>&quot;_id&quot;: { &quot;$oid &quot;: &quot;0080a245fabe57999707dc41ced60edc4ac7ac40&quot; }</code> ==&gt; <code>&quot;_id&quot;:[00 80 A2 45 FA BE 57 99 97 07 DC 41 CE D6 0E DC 4A C7 AC 40]</code>
        ///     <code>$numberint64</code> or <code>$numberlong</code>,
        ///     <code>$numberint32</code>, or <code>$numberint</code>,
        ///     <code>$numberdecimal</code>,
        ///     <code>$numberdouble</code>,
        ///     <code>$numberfloat</code> or <code>$single</code>,
        ///     <code>$numberint16</code> or <code>$numbershort</code>,
        ///     <code>$numberuint32</code> or <code>$numberuint</code>,
        ///     <code>$numberuint64</code> or <code>$numberulong</code>,
        ///     <code>$numberuint16</code> or <code>$numberushort</code>,
        ///     <code>$bool</code> or <code>$boolean</code>;
        /// </remarks>
        public int FromJson(string json,
                                [AllowNull]
                                dynamic primaryKey,
                                string pkPropertyName = "_id",
                                string jsonBinName = null,
                                WritePolicy writePolicy = null,
                                TimeSpan? ttl = null,
                                bool writePKPropertyName = false)
        {
            return this.SetAccess.FromJson(this.SetName,
                                            json,
                                            primaryKey,
                                            pkPropertyName: pkPropertyName,
                                            jsonBinName: jsonBinName,
                                            writePolicy: writePolicy,
                                            ttl: ttl,
                                            writePKPropertyName: writePKPropertyName);
        }

        #endregion

        #region Overrides
        public override bool Equals(object obj)
        {
            if(obj is SetRecords set) return this.Equals(set);
            if(obj is ARecord rec) return this.Equals(rec);

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return Helpers.GetStableHashCode(this.ToString());
        }

        public override string ToString()
        {
            if(this._bins == null || this._bins.Length == 0)
                return $"{this.Namespace}.{this.SetName}";

            return $"{this.Namespace}.{this.SetName}{{{string.Join(',',this._bins)}}}";
        }

        #endregion

        #region IEquatable

        public bool Equals([AllowNull] ARecord other)
        {
            if(other is null) return false;
            if(ReferenceEquals(other, this)) return true;

            return this.SetName == other.Aerospike.SetName && this.Namespace == other.Aerospike.Namespace;
        }

        public bool Equals([AllowNull] SetRecords other)
        {
            if (other is null) return false;
            if (ReferenceEquals(other, this)) return true;

            return this.SetName == other.SetName && this.Namespace == other.Namespace;
        }

        #endregion

        #region IEnumerable
        public IEnumerator<ARecord> GetEnumerator()
        {
            using var recordset = this.SetAccess.AerospikeConnection
                                    .AerospikeClient
                                    .Query(this.DefaultQueryPolicy,
                                            string.IsNullOrEmpty(this.SetName) || this.SetName == LPSet.NullSetName
                                            ? new Statement() { Namespace = this.Namespace }
                                            : new Statement() { Namespace = this.Namespace, SetName = this.SetName });

            while (recordset.Next())
            {                    
                yield return new ARecord(this.SetAccess,
                                            recordset.Key,
                                            recordset.Record,
                                            this._bins,
                                            setBinsHashCode: this.BinsHashCode,
                                            dumpType: this.DefaultRecordView);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
        #endregion
    }
}
