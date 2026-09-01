using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace StickyNotes.Views;

/// <summary>Painel de grade com virtualização real para o mural "Todas as notas":
/// só os post-its visíveis (± uma linha) existem na árvore visual — o WrapPanel
/// anterior criava ~15 elementos para TODAS as notas, mesmo fora da tela.
/// Implementa IScrollInfo: o próprio painel controla o offset de scroll (em pixels)
/// e se traduz com um TranslateTransform, posicionando os filhos em coordenadas
/// virtuais — o mesmo mecanismo do VirtualizingStackPanel nativo.</summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(206.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(206.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Tamanho da célula (pitch) entre post-its: conteúdo 190 + margem 8
    /// de cada lado, igual ao DataTemplate do mural.</summary>
    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    private readonly TranslateTransform _translate = new();
    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);
    private Point _offset;

    public VirtualizingWrapPanel()
    {
        // O offset do scroll é aplicado como transform no painel inteiro — os filhos
        // são arranjados nas coordenadas virtuais (linha × altura da célula).
        RenderTransform = _translate;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateScrollInfo(availableSize);
        var (firstIndex, lastIndex) = GetVisibleRange();

        UIElementCollection children = InternalChildren;
        IItemContainerGenerator generator = ItemContainerGenerator;

        // Posição do gerador do primeiro item visível; childIndex é onde o container
        // entra na coleção interna (realizado = position.Index, senão Index + 1).
        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                bool newlyRealized;
                var child = (UIElement)generator.GenerateNext(out newlyRealized)!;
                if (newlyRealized)
                {
                    if (childIndex >= children.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(CellWidth, CellHeight));
            }
        }

        CleanUpItems(firstIndex, lastIndex);

        // Com IScrollInfo, o ScrollViewer se guia por Viewport/Extent declarados via
        // InvalidateScrollInfo — o retorno é o viewport (o painel nunca é maior que ele).
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateScrollInfo(finalSize);

        IItemContainerGenerator generator = ItemContainerGenerator;
        int columns = CalculateColumns(finalSize.Width);
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            int row = itemIndex / columns;
            int column = itemIndex % columns;
            InternalChildren[i].Arrange(new Rect(column * CellWidth, row * CellHeight, CellWidth, CellHeight));
        }

        return finalSize;
    }

    /// <summary>Descarta os containers fora da janela visível (re-virtualiza).</summary>
    private void CleanUpItems(int firstVisibleIndex, int lastVisibleIndex)
    {
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator generator = ItemContainerGenerator;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstVisibleIndex && itemIndex <= lastVisibleIndex)
            {
                continue;
            }

            // itemIndex < 0: container órfão (o mapa do gerador foi resetado junto
            // com a coleção) — sai da árvore sem passar pelo gerador.
            if (itemIndex >= 0)
            {
                generator.Remove(position, 1);
            }

            RemoveInternalChildRange(i, 1);
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;

            case NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                break;
        }
    }

    // --- Layout ---

    /// <summary>Post-its por linha: mesmo reflow do WrapPanel (floor(largura / célula)).</summary>
    private int CalculateColumns(double width) =>
        double.IsInfinity(width) || width <= 0
            ? 1
            : Math.Max(1, (int)Math.Floor(width / CellWidth));

    private (int First, int Last) GetVisibleRange()
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        int itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;
        if (itemCount == 0)
        {
            return (0, -1);
        }

        int columns = CalculateColumns(_extent.Width);
        int first = (int)Math.Floor(_offset.Y / CellHeight) * columns;
        int last = (int)Math.Ceiling((_offset.Y + _viewport.Height) / CellHeight) * columns - 1;
        return (Math.Max(0, first), Math.Min(itemCount - 1, last));
    }

    /// <summary>Recalcula extent/viewport e publica no ScrollOwner (barra de rolagem).</summary>
    private void UpdateScrollInfo(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        int itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;

        int columns = CalculateColumns(availableSize.Width);
        int rows = (int)Math.Ceiling(itemCount / (double)columns);
        var extent = new Size(Math.Max(availableSize.Width, 0), rows * CellHeight);
        if (extent != _extent)
        {
            _extent = extent;
            ScrollOwner?.InvalidateScrollInfo();
        }

        var viewport = new Size(
            double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height);
        if (viewport != _viewport)
        {
            _viewport = viewport;
            ScrollOwner?.InvalidateScrollInfo();
        }

        // Itens removidos com o mural rolado para o fim: o offset não pode passar
        // do novo fim do conteúdo.
        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxOffset)
        {
            _offset.Y = maxOffset;
            _translate.Y = -maxOffset;
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    // --- IScrollInfo (scroll vertical em pixels; horizontal desativado) ---

    public ScrollViewer ScrollOwner { get; set; } = null!;

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public void LineUp() => SetVerticalOffset(VerticalOffset - 16);

    public void LineDown() => SetVerticalOffset(VerticalOffset + 16);

    public void LineLeft() { }

    public void LineRight() { }

    public void PageUp() => SetVerticalOffset(VerticalOffset - _viewport.Height);

    public void PageDown() => SetVerticalOffset(VerticalOffset + _viewport.Height);

    public void PageLeft() { }

    public void PageRight() { }

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - MouseWheelDelta);

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + MouseWheelDelta);

    public void MouseWheelLeft() { }

    public void MouseWheelRight() { }

    /// <summary>Roda do mouse: ~48px por tick (3 "linhas" de 16px, padrão do Windows).</summary>
    private const double MouseWheelDelta = 48;

    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        offset = Math.Clamp(offset, 0, maxOffset);
        if (offset == _offset.Y)
        {
            return;
        }

        _offset.Y = offset;
        _translate.Y = -offset;
        ScrollOwner?.InvalidateScrollInfo();

        // Força a re-realização da janela visível para o novo offset.
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement child)
        {
            // VirtualizingPanel.ItemContainerGenerator é tipado como a interface;
            // IndexFromContainer vive na classe concreta.
            var concreteGenerator = (ItemContainerGenerator)ItemContainerGenerator;
            int itemIndex = concreteGenerator.IndexFromContainer(child);
            if (itemIndex >= 0)
            {
                int columns = CalculateColumns(_extent.Width);
                double top = itemIndex / columns * CellHeight;
                double bottom = top + CellHeight;
                if (top < _offset.Y)
                {
                    SetVerticalOffset(top);
                }
                else if (bottom > _offset.Y + _viewport.Height)
                {
                    SetVerticalOffset(bottom - _viewport.Height);
                }

                return new Rect(rectangle.X, top - _offset.Y, rectangle.Width,
                    Math.Min(rectangle.Height, _viewport.Height));
            }
        }

        return rectangle;
    }
}
