﻿using System.Collections.Generic;
using Umbraco.Core.Cache;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Persistence.Repositories
{
    internal abstract class RecycleBinRepository<TId, TEntity, TRepository> : VersionableRepositoryBase<TId, TEntity, TRepository>, IRecycleBinRepository<TEntity>
        where TEntity : class, IUmbracoEntity
        where TRepository :  class, IRepository
    {
        protected RecycleBinRepository(IScopeUnitOfWork work, CacheHelper cache, ILogger logger)
            : base(work, cache, logger)
        { }

        protected abstract int RecycleBinId { get; }

        public virtual IEnumerable<TEntity> GetEntitiesInRecycleBin()
        {
            return GetByQuery(Query<TEntity>().Where(entity => entity.Trashed));
        }
    }
}
