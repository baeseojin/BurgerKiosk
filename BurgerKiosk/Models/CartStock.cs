namespace BurgerKiosk.Models;

public static class CartStock
{
    public static bool CanFulfill(IEnumerable<CartItemModel> cart, IEnumerable<MenuItemModel> menus)
    {
        var products=menus.ToDictionary(x=>x.Id);
        var required=new Dictionary<long,int>();
        foreach(var line in cart)
        {
            if(line.Quantity<1 || !products.TryGetValue(line.Menu.Id,out var product)) return false;
            long[] ids;
            if(product.Category=="세트")
            {
                var burger=menus.FirstOrDefault(x=>x.Id==product.BurgerProductId && x.Category=="버거");
                var side=menus.FirstOrDefault(x=>x.SetOptionKey==line.Side && x.Category=="사이드");
                var drink=menus.FirstOrDefault(x=>x.SetOptionKey==line.Drink && x.Category=="음료");
                if(burger==null || side==null || drink==null) return false;
                ids=[burger.Id,side.Id,drink.Id];
            }
            else ids=[product.Id];
            foreach(var id in ids) required[id]=required.GetValueOrDefault(id)+line.Quantity;
        }
        return required.All(x=>products[x.Key].StockQuantity>=x.Value);
    }

    public static bool CanAdd(IEnumerable<CartItemModel> cart,IEnumerable<MenuItemModel> menus,MenuItemModel product,int count,string side="",string drink="")
    {
        if(count<1) return false;
        var sides=product.Category=="세트" && side=="" ? new[]{"감자튀김","치즈스틱"} : new[]{side};
        var drinks=product.Category=="세트" && drink=="" ? new[]{"콜라","사이다"} : new[]{drink};
        return sides.Any(s=>drinks.Any(d=>CanFulfill(cart.Append(new CartItemModel(product,product.Category=="세트"?"세트":"",s,d){Quantity=count}),menus)));
    }

    public static int AvailableSets(MenuItemModel set,IEnumerable<MenuItemModel> menus)
    {
        var burger=menus.FirstOrDefault(x=>x.Id==set.BurgerProductId && x.Category=="버거");
        int sides=menus.Where(x=>x.Category=="사이드" && x.SetOptionKey is "감자튀김" or "치즈스틱").Select(x=>x.StockQuantity).DefaultIfEmpty().Max();
        int drinks=menus.Where(x=>x.Category=="음료" && x.SetOptionKey is "콜라" or "사이다").Select(x=>x.StockQuantity).DefaultIfEmpty().Max();
        return Math.Min(burger?.StockQuantity??0,Math.Min(sides,drinks));
    }
}
