using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.XnkSavePosted;

public sealed class DocumentValidationTests
{
    private static CWarehouseDocument New_Document() => new()
    {
        So_Phieu="N-1",Kho_ID=1,NCC_ID=1,Is_Receipt=true,
        Details=[new() { San_Pham_ID=1,So_Luong=10,Don_Gia=2 }]
    };

    [Fact]
    public void Complete_document_is_valid_and_total_is_derived_from_quantity_and_price()
    {
        var v_objData=New_Document();
        CXNK_Document_Controller.Check_Data(v_objData,"tester");
        Assert.Equal(20,v_objData.Details[0].Tri_Gia);
    }

    [Theory]
    [InlineData("login")][InlineData("request")][InlineData("id")][InlineData("version")]
    [InlineData("number")][InlineData("number-length")][InlineData("warehouse")][InlineData("date")]
    [InlineData("supplier")][InlineData("note")][InlineData("token")][InlineData("empty-lines")]
    [InlineData("null-lines")][InlineData("product")][InlineData("quantity")][InlineData("quantity-scale")]
    [InlineData("quantity-max")][InlineData("price")][InlineData("price-scale")][InlineData("price-max")]
    [InlineData("line-id")][InlineData("duplicate-id")][InlineData("null-line")]
    public void Invalid_document_fails_before_opening_a_database_connection(string p_strCase)
    {
        var v_objData=New_Document(); string v_strLogin="tester";
        switch(p_strCase)
        {
            case "login":v_strLogin=" ";break;
            case "request":v_objData.Request_ID=Guid.Empty;break;
            case "id":v_objData.Auto_ID=-1;break;
            case "version":v_objData.Document_Version=-1;break;
            case "number":v_objData.So_Phieu=" ";break;
            case "number-length":v_objData.So_Phieu=new('a',101);break;
            case "warehouse":v_objData.Kho_ID=0;break;
            case "date":v_objData.Ngay_Chung_Tu=default;break;
            case "supplier":v_objData.NCC_ID=0;break;
            case "note":v_objData.Ghi_Chu=new('a',1001);break;
            case "token":v_objData.Auto_ID=1;break;
            case "empty-lines":v_objData.Details.Clear();break;
            case "null-lines":v_objData.Details=null!;break;
            case "product":v_objData.Details[0].San_Pham_ID=0;break;
            case "quantity":v_objData.Details[0].So_Luong=0;break;
            case "quantity-scale":v_objData.Details[0].So_Luong=1.0001m;break;
            case "quantity-max":v_objData.Details[0].So_Luong=1000000000000000m;break;
            case "price":v_objData.Details[0].Don_Gia=0;break;
            case "price-scale":v_objData.Details[0].Don_Gia=1.001m;break;
            case "price-max":v_objData.Details[0].Don_Gia=10000000000000000m;break;
            case "line-id":v_objData.Details[0].Auto_ID=-1;break;
            case "duplicate-id":v_objData.Details[0].Auto_ID=1;v_objData.Details.Add(new(){Auto_ID=1,San_Pham_ID=1,So_Luong=1,Don_Gia=1});break;
            case "null-line":v_objData.Details[0]=null!;break;
        }
        Assert.Throws<InvalidDataException>(()=>CXNK_Document_Controller.Check_Data(v_objData,v_strLogin));
    }

    [Fact]
    public void Null_document_is_rejected() => Assert.Throws<ArgumentNullException>(()=>CXNK_Document_Controller.Check_Data(null!,"tester"));
}
