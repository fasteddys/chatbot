namespace GetContext
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("public.questions")]
    public partial class questions
    {
        public int id { get; set; }

        public bool is_biomark { get; set; }

        [StringLength(8000)]
        public string question { get; set; }

        public int? id_biomark { get; set; }

        [StringLength(8000)]
        public string info { get; set; }

        public virtual biomarks biomarks { get; set; }
    }
}
