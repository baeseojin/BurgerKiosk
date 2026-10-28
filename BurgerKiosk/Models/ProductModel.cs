using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace BurgerKiosk.Models
{
    [Table("products")]
    public class ProductModel : BaseModel
    {
        [PrimaryKey("id")]
        public long Id { get; set; }

        [Column("set_option_key")]
        public string? SetOptionKey { get; set; }

        [Column("burger_product_id")]
        public long? BurgerProductId { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("category")]
        public string Category { get; set; } = "";

        [Column("price")]
        public int Price { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("image_url")]
        public string? ImageUrl { get; set; }

        [Column("is_available")]
        public bool IsAvailable { get; set; }

        [Column("stock_quantity")]
        public int StockQuantity { get; set; }

        [Column("is_recommended")]
        public bool IsRecommended { get; set; }
    }
}
