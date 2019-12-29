using System;

namespace Demo.SinjulMSBH.Entities
{
	public interface ISyncableEntity
	{
		Guid Id { get; set; }

		DateTime CreatedAt { get; set; }
		DateTime ChangedAt { get; set; }
		DateTime? SyncedAt { get; set; }

		bool IsDeleted { get; set; }
	}
}
