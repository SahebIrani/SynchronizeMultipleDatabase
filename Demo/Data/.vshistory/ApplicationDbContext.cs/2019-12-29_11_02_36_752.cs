using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Demo.SinjulMSBH.Entities;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Demo.Data
{
	public class ApplicationDbContext : IdentityDbContext
	{
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
			: base(options)
		{
		}

		public bool KeepTimeStamps { get; private set; } = false;

		public override int SaveChanges(bool acceptAllChangesOnSuccess)
		{
			LogTimeStamps();
			return base.SaveChanges(acceptAllChangesOnSuccess);
		}

		public override int SaveChanges()
		{
			LogTimeStamps();
			return base.SaveChanges();
		}

		public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
		{
			LogTimeStamps();
			return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
		}

		public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			LogTimeStamps();
			return base.SaveChangesAsync(cancellationToken);
		}

		private void LogTimeStamps()
		{
			if (KeepTimeStamps) return;

			var now = DateTime.UtcNow;
			var added = ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToList();
			var changed = ChangeTracker.Entries().Where(x => x.State == EntityState.Modified).ToList();

			foreach (var entry in added.Where(x => x.Entity is ISyncableEntity))
			{
				var entity = entry.Entity as ILogTimeStamps;
				entity.CreatedAt = now;
				entity.ChangedAt = now;
			}
			foreach (var entry in changed.Where(x => x.Entity is ISyncableEntity))
			{
				(entry.Entity as ILogTimeStamps).ChangedAt = now;
			}
		}

		public static async Task SyncTablesAsync<T>(
			SyncStrategy strategy,
			DateTime from,
			DateTime to,
			DbSet<T> hereTable,
			DbSet<T> thereTable,
			params string[] excludes) where T : class, ISyncableEntity, new()
		{
			var changedHere = await hereTable
				.Where(s => s.ChangedAt > from && s.ChangedAt <= to
						|| s.CreatedAt > from && s.CreatedAt <= to).ToListAsync();

			var changedThere = await thereTable
				.Where(s => s.ChangedAt > from && s.ChangedAt <= to
						|| s.CreatedAt > from && s.CreatedAt <= to
						|| s.SyncedAt > from && s.SyncedAt <= to).ToListAsync();

			var modifiedAtSameTimeOnBothSides = changedThere
				.Select(c => new { c.Id, c.ChangedAt })
				.Where(there => changedHere.Any(here => here.Id == there.Id
												&& here.ChangedAt == there.ChangedAt))
				.Select(i => i.Id).ToList();

			changedHere = changedHere
				.Where(h => !modifiedAtSameTimeOnBothSides.Contains(h.Id)).ToList();

			changedThere = changedThere
				.Where(h => !modifiedAtSameTimeOnBothSides.Contains(h.Id)).ToList();

			var conflictIds = changedThere
				.Select(c => c.Id)
				.Where(thereId => changedHere.Any(here => here.Id == thereId)).ToList();

			if (conflictIds.Any())
			{
				switch (strategy)
				{
					case SyncStrategy.Stop:
						throw new ApplicationException("Sync failed, nothing changed in either DB");

					case SyncStrategy.OverrideAzure:
						foreach (var id in conflictIds)
						{
							changedThere.Remove(changedThere.First(h => h.Id == id));
						}
						break;
					case SyncStrategy.OverrideLocal:
						foreach (var id in conflictIds)
						{
							changedHere.Remove(changedHere.First(h => h.Id == id));
						}
						break;
					case SyncStrategy.OverrideOld:
						foreach (var id in conflictIds)
						{
							var here = changedHere.First(h => h.Id == id);
							var there = changedThere.First(h => h.Id == id);
							if (there.ChangedAt > here.ChangedAt)
							{
								changedHere.Remove(here);
							}
							else
							{
								changedThere.Remove(there);
							}
						}
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null);
				}
			}

			var utcNow = DateTime.UtcNow;
			changedHere.ForEach(changed => changed.SyncedAt = utcNow);
			AddOrUpdateEntities(thereTable, changedHere, excludes);
			AddOrUpdateEntities(hereTable, changedThere, excludes);
		}

		private static void AddOrUpdateEntities<T>(
			DbSet<T> destinationTable,
			IEnumerable<T> entityList,
			params string[] excludes)
			where T : class, ISyncableEntity, new()
		{
			foreach (var changed in entityList)
			{
				var target = new T();
				Mapper.Map(changed, target, excludes);
				destinationTable.AddOrUpdate(target);
			}
		}

		public static void AddOrUpdate<T>(this DbSet<T> dbSet, T data) where T : class
		{
			var context = dbSet.GetContext();
			var ids = context.Model.FindEntityType(typeof(T)).FindPrimaryKey().Properties.Select(x => x.Name);

			var t = typeof(T);
			List<PropertyInfo> keyFields = new List<PropertyInfo>();

			foreach (var propt in t.GetProperties())
			{
				var keyAttr = ids.Contains(propt.Name);
				if (keyAttr)
				{
					keyFields.Add(propt);
				}
			}
			if (keyFields.Count <= 0)
			{
				throw new Exception($"{t.FullName} does not have a KeyAttribute field. Unable to exec AddOrUpdate call.");
			}
			var entities = dbSet.AsNoTracking().ToList();
			foreach (var keyField in keyFields)
			{
				var keyVal = keyField.GetValue(data);
				entities = entities.Where(p => p.GetType().GetProperty(keyField.Name).GetValue(p).Equals(keyVal)).ToList();
			}
			var dbVal = entities.FirstOrDefault();
			if (dbVal != null)
			{
				context.Entry(dbVal).CurrentValues.SetValues(data);
				context.Entry(dbVal).State = EntityState.Modified;
				return;
			}
			dbSet.Add(data);
		}

		public static void AddOrUpdate<T>(this DbSet<T> dbSet, Expression<Func<T, object>> key, T data) where T : class
		{
			var context = GetContext(dbSet);
			var ids = context.Model.FindEntityType(typeof(T)).FindPrimaryKey().Properties.Select(x => x.Name);
			var t = typeof(T);
			var keyObject = key.Compile()(data);
			PropertyInfo[] keyFields = keyObject.GetType().GetProperties().Select(p => t.GetProperty(p.Name)).ToArray();
			if (keyFields == null)
			{
				throw new Exception($"{t.FullName} does not have a KeyAttribute field. Unable to exec AddOrUpdate call.");
			}
			var keyVals = keyFields.Select(p => p.GetValue(data));
			var entities = dbSet.AsNoTracking().ToList();
			int i = 0;
			foreach (var keyVal in keyVals)
			{
				entities = entities.Where(p => p.GetType().GetProperty(keyFields[i].Name).GetValue(p).Equals(keyVal)).ToList();
				i++;
			}
			if (entities.Any())
			{
				var dbVal = entities.FirstOrDefault();
				var keyAttrs =
					data.GetType().GetProperties().Where(p => ids.Contains(p.Name)).ToList();
				if (keyAttrs.Any())
				{
					foreach (var keyAttr in keyAttrs)
					{
						keyAttr.SetValue(data,
							dbVal.GetType()
								.GetProperties()
								.FirstOrDefault(p => p.Name == keyAttr.Name)
								.GetValue(dbVal));
					}
					context.Entry(dbVal).CurrentValues.SetValues(data);
					context.Entry(dbVal).State = EntityState.Modified;
					return;
				}
			}
			dbSet.Add(data);
		}
		public static DbContext GetContext<TEntity>(this DbSet<TEntity> dbSet) where TEntity : class
		{
			return (DbContext)dbSet
				.GetType().GetTypeInfo()
				.GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)
				.GetValue(dbSet);
		}

		public static void AddOrUpdate(this DbContext ctx, object entity)
		{
			var entry = ctx.Entry(entity);
			switch (entry.State)
			{
				case EntityState.Detached:
					ctx.Add(entity);
					break;
				case EntityState.Modified:
					ctx.Update(entity);
					break;
				case EntityState.Added:
					ctx.Add(entity);
					break;
				case EntityState.Unchanged:
					//item already in db no need to do anything
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}






			static async Task TrySyncDayByDayAsync()
			{
				try
				{
					var lastSyncedAt = ReadLastSyncedTime();
					var strategy = SyncStrategy.OverrideOld;
					var hereConnectionString = ConfigurationManager.AppSettings["LocalConnectionString"];
					var thereConnectionString = ConfigurationManager.AppSettings["RemoteConnectionString"];

					var hereDb = new SimpleContext(hereConnectionString) { KeepTimeStamps = true };
					var thereDb = new SimpleContext(thereConnectionString) { KeepTimeStamps = true };
					var now = DateTime.UtcNow;

					while (lastSyncedAt < now)
					{
						var syncFrom = lastSyncedAt;
						var tomorrow = lastSyncedAt.AddDays(1);
						var syncTo = tomorrow > now ? now : tomorrow;

						await SyncTablesAsync(strategy, syncFrom, syncTo, hereDb.Persons, thereDb.Persons);
						await SyncTablesAsync(strategy, syncFrom, syncTo, hereDb.Pages, thereDb.Pages);
						await hereDb.SaveChangesAsync();
						await thereDb.SaveChangesAsync();

						await SyncTablesAsync(strategy, syncFrom, syncTo, hereDb.Books, thereDb.Books);
						await hereDb.SaveChangesAsync();
						await thereDb.SaveChangesAsync();

						await SyncTablesAsync(strategy, syncFrom, syncTo, hereDb.Shelves, thereDb.Shelves);
						await hereDb.SaveChangesAsync();
						await thereDb.SaveChangesAsync();

						WriteLastSyncedTime(syncTo);
						lastSyncedAt = syncTo;
					}
				}
				catch (Exception e)
				{
					_logger.Error(e, $"Error while syncing. Error: {e.Message} - InnerException: {e.InnerException?.Message}");
				}
			}

		public enum SyncStrategy
		{
			Stop,
			OverrideAzure,
			OverrideLocal,
			OverrideOld
		}

		public class Shelf : ISyncableEntity
		{
			public Guid Id { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime ChangedAt { get; set; }
			public DateTime? SyncedAt { get; set; }
			public bool IsDeleted { get; set; }

			[Required]
			public Book Book { get; set; }
		}

		public class Book : ISyncableEntity
		{
			public Guid Id { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime ChangedAt { get; set; }
			public DateTime? SyncedAt { get; set; }
			public bool IsDeleted { get; set; }

			[Required]
			public Page Page { get; set; }
		}

		public class Page : ISyncableEntity
		{
			public Guid Id { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime ChangedAt { get; set; }
			public DateTime? SyncedAt { get; set; }
			public bool IsDeleted { get; set; }
		}

		public class Person : ISyncableEntity
		{
			public Guid Id { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime ChangedAt { get; set; }
			public DateTime? SyncedAt { get; set; }
			public bool IsDeleted { get; set; }
		}
	}
}
