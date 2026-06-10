using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShoppingCart : MonoBehaviour
{
    [Serializable]
    public class CartLine
    {
        public string productId;
        public string displayName;
        public int count;
        public int unitPrice;
    }

    public TextMeshProUGUI textMeshProText;
    public Text legacyText;
    public bool logAdds = true;

    readonly List<CartLine> lines = new List<CartLine>();
    bool checkoutComplete;
    int lastCheckoutTotal;

    public IReadOnlyList<CartLine> Lines => lines;

    public void AddProduct(ShopProduct product)
    {
        if (product == null)
            return;

        checkoutComplete = false;
        var id = string.IsNullOrWhiteSpace(product.productId) ? product.name : product.productId;
        var line = lines.Find(item => item.productId == id);
        if (line == null)
        {
            line = new CartLine
            {
                productId = id,
                displayName = product.GetCartLabel(),
                unitPrice = product.price
            };
            lines.Add(line);
        }

        line.count++;
        RefreshDisplay();

        if (logAdds)
            Debug.Log($"Added to cart: {line.displayName} x{line.count}", product);
    }

    public bool Checkout()
    {
        if (lines.Count == 0)
            return false;

        lastCheckoutTotal = CalculateTotal();
        checkoutComplete = true;
        lines.Clear();
        RefreshDisplay();

        if (logAdds)
            Debug.Log($"Checkout complete: ${lastCheckoutTotal}");

        return true;
    }

    public void Clear()
    {
        checkoutComplete = false;
        lines.Clear();
        RefreshDisplay();
    }

    public void RefreshDisplay()
    {
        var text = BuildCartText();

        if (textMeshProText != null)
            textMeshProText.text = text;

        if (legacyText != null)
            legacyText.text = text;
    }

    string BuildCartText()
    {
        if (checkoutComplete)
            return $"Checkout Complete\nPaid ${lastCheckoutTotal}\nThank you";

        if (lines.Count == 0)
            return "Cart\n- Empty -";

        var builder = new StringBuilder();
        builder.AppendLine("Cart");

        foreach (var line in lines)
        {
            var subtotal = line.unitPrice * line.count;
            builder.Append(line.displayName);
            builder.Append(" x");
            builder.Append(line.count);
            if (line.unitPrice > 0)
            {
                builder.Append("  $");
                builder.Append(subtotal);
            }
            builder.AppendLine();
        }

        var total = CalculateTotal();
        if (total > 0)
        {
            builder.Append("Total $");
            builder.Append(total);
        }

        return builder.ToString();
    }

    int CalculateTotal()
    {
        var total = 0;
        foreach (var line in lines)
            total += line.unitPrice * line.count;

        return total;
    }
}
