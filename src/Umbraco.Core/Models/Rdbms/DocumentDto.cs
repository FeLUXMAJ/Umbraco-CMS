﻿using System;
using NPoco;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace Umbraco.Core.Models.Rdbms
{
    [TableName(TableName)]
    [PrimaryKey("nodeId", AutoIncrement = false)]
    [ExplicitColumns]
    internal class DocumentDto
    {
        private const string TableName = Constants.DatabaseSchema.Tables.Document;

        [Column("nodeId")]
        [PrimaryKeyColumn(AutoIncrement = false)]
        [ForeignKey(typeof(ContentDto))]
        public int NodeId { get; set; }

        [Column("published")]
        [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_Published")]
        public bool Published { get; set; }

        [Column("releaseDate")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? ReleaseDate { get; set; }

        [Column("expireDate")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? ExpiresDate { get; set; }

        [ResultColumn]
        [Reference(ReferenceType.OneToOne, ReferenceMemberName = "NodeId")]
        public ContentDto ContentDto { get; set; }

        // although a content has many content versions,
        // they can only be loaded one by one (as several content)
        [ResultColumn]
        [Reference(ReferenceType.OneToOne, ReferenceMemberName = "NodeId")]
        public DocumentVersionDto DocumentVersionDto { get; set; }
    }
}
