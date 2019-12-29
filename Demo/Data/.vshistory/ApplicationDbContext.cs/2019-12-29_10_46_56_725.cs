using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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

		public override int SaveChanges()
		{
			LogTimeStamps();
			return base.SaveChanges();
		}
		public override Task<int> SaveChangesAsync()
		{
			LogTimeStamps();
			return base.SaveChangesAsync();
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
			IDbSet<T> hereTable,
			IDbSet<T> thereTable,
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
			IDbSet<T> destinationTable,
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
