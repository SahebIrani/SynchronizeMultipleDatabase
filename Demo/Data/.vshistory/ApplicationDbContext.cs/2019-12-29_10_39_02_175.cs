using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

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

		public class Page : ISyncableEntity
		{
			public Guid Id { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime ChangedAt { get; set; }
			public DateTime? SyncedAt { get; set; }
			public bool IsDeleted { get; set; }
		}
		public class Book : ISyncableEntity
		{
			public Guid Id { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime ChangedAt { get; set; }
			public DateTime? SyncedAt { get; set; }
			public bool IsDeleted { get; set; }
		}
	}
}
