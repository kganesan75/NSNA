﻿using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using PN2016.DBModels;
using PN2016.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web.Mvc;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PN2016.Controllers
{
    public class DirectoryController : BaseController
    {

        string[] allowedExtension = new string[] { ".jpg", ".png", ".gif", ".jpeg" };
        public ActionResult Index()
        {
            return RedirectToAction("Create");
        }

        public ActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Create(ContactInfoViewModel model)
        {
            ValidateModel(model);
            
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            //Process Data to Create Data Model
            FamilyInfoDBModel familyContact = ProcessViewModeltoDBModel(model);

            //Insert into DB
            ContactInfoDB contactInfoDB = new ContactInfoDB();
            contactInfoDB.InsertFamilyInfo(familyContact);

            //Upload Pic to Azure.
            var familyPic = model.FamilyPic;
            if (familyPic != null && familyPic.ContentLength != 0 && !string.IsNullOrWhiteSpace(familyContact.FamilyPicFileName))
            {
                var mediaFileName = familyContact.FamilyPicFileName;
                new AzureFileStorage().UploadFile(mediaFileName, familyPic.InputStream);
            }
            
            //Send Email.
            GMailDispatcher mailDispatcher = new GMailDispatcher();
            mailDispatcher.SendCreateConfirmMsg(familyContact.Email, familyContact.FirstName+ " "+ familyContact.LastName, familyContact.FamilyContactGuid);
            
            return View("CreateConfirm");
        }

        public ActionResult List786()
        {
            ContactInfoDB contactInfoDB = new ContactInfoDB();
            var allContacts = contactInfoDB.SelectAllforList();
            return View("List", allContacts);
        }

        public ActionResult List()
        {
            return View("adminLogin");
        }

        public ActionResult PdfPreview()
        {
            var contacts = new ContactInfoDB().SelectAllFamilyInfo();
            return View(contacts);
        }
        public FileStreamResult PrintPdf()
        {
            var outputDoc = new PdfDocument();
            
            outputDoc.Info.Title = "NSNA-NE 2017 Directory";
            outputDoc.Info.Author = "Karuppiah Ganesan";
            outputDoc.Info.CreationDate = DateTime.Parse("01-01-2017");
            outputDoc.Info.ModificationDate = DateTime.Today;
            outputDoc.Info.Subject = "NSNA-NE 2017 Directory";
            
            AddPdfPage(GetCoverPageHtml, outputDoc);

            var contacts = new ContactInfoDB().SelectAllFamilyInfo();
            foreach (var contact in contacts.OrderBy(i => i.FirstName))
            {
                AddPdfPage(() => GetContactInfoHtml(contact), outputDoc);
            }

            AddPdfPage(GetEndPageHtml, outputDoc);
            
            MemoryStream ms = new MemoryStream();
            outputDoc.Save(ms, false);
            return new FileStreamResult(ms, "application/pdf");

        }

        private void AddPdfPage( Func<string> getHtmlDoc , PdfDocument document)
        {
            var htmlDoc = getHtmlDoc();
            if(htmlDoc == null)
                return;
            var generatedPdfDoc = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(htmlDoc, PdfSharp.PageSize.A4);
            using (var pdfstream = new MemoryStream())
            {
                generatedPdfDoc.Save(pdfstream, false);
                var importedPdfDoc = PdfReader.Open(pdfstream, PdfDocumentOpenMode.Import);
                foreach (PdfPage pdfPage in importedPdfDoc.Pages)
                {
                    document.AddPage(pdfPage);
                }
            }
        }

        private string GetContactInfoHtml(FamilyInfoDBModel familyInfo)
        {
           var htmlDoc =  RenderPartialToString("FamilyPdfView", familyInfo);
           return htmlDoc;
        }

        private string GetCoverPageHtml()
        {
            var htmlDoc = $"<html><body><h2 align='center' style='color:red'>2017 NSNA Nagarathar Directory</h2></body></html>";
            return htmlDoc;
        }

        private string GetEndPageHtml()
        {
            var htmlDoc = $"<html><body><h2 align='center' style='color:red'>Thanks</h2></body></html>";
            return htmlDoc;
        }

        [HttpPost]
        public ActionResult List(AdminLoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("adminLogin", model);
            }
            if (!AuthAdmin(model.UserName, model.Password))
            {
                ModelState.AddModelError("Login", "Username Password is invalid");
                return View("adminLogin", model);
            }
            return List786();
        }

        private bool AuthAdmin(string username, string password)
        {
            return (username == "admin" && password == "2017PN@dmin");
        }

        public ActionResult Detail(string id)
        {
            if(string.IsNullOrEmpty(id))
            {
                return View("Error", new HandleErrorInfo(new Exception("Id is invalid."), "Directory", "Detail"));
            }

            ContactInfoDB contactInfo = new ContactInfoDB();
            FamilyInfoDBModel familyInfoDBModel = contactInfo.SelectWithKidsInfo(id);
            if (familyInfoDBModel == null || familyInfoDBModel.FamilyContactGuid == null)
            {
                return View("Error", new HandleErrorInfo(new Exception("User Id not found."), "Directory", "Detail"));
            }
            else
            {
                ContactInfoViewModel viewModel = ProcessDBModeltoViewModel(familyInfoDBModel);
                if(viewModel == null)
                {
                    return View("Error", new HandleErrorInfo(new Exception("Error Processing Data."), "Directory", "Detail"));
                }
                else
                {
                    return View(viewModel);
                }
            }
        }

        public ActionResult Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return View("Error", new HandleErrorInfo(new Exception("Id is invalid."), "Directory", "Detail"));
            }

            ContactInfoDB contactInfo = new ContactInfoDB();
            FamilyInfoDBModel familyInfoDBModel = contactInfo.SelectWithKidsInfo(id);
            if (familyInfoDBModel == null || familyInfoDBModel.FamilyContactGuid == null)
            {
                return View("Error", new HandleErrorInfo(new Exception("User Id not found."), "Directory", "Detail"));
            }
            else
            {
                ContactInfoViewModel viewModel = ProcessDBModeltoViewModel(familyInfoDBModel);
                if (viewModel == null)
                {
                    return View("Error", new HandleErrorInfo(new Exception("Error Processing Data."), "Directory", "Detail"));
                }
                var kidsCount = (viewModel.Kids == null) ? 0 : viewModel.Kids.Count;
                //Fill in the rest of the Kids field with empty string for view
                for (int i = 0; i < (5 - kidsCount); i++)
                {
                    viewModel.Kids.Add(new KidsViewModel());
                }
                return View(viewModel);
            }
        }

        [HttpPost]
        public ActionResult Edit(ContactInfoViewModel model)
        {
            ValidateModel(model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }
            
            //Process Data to Create Data Model
            FamilyInfoDBModel familyContact = ProcessViewModeltoDBModel(model);

            //Insert into DB
            ContactInfoDB contactInfoDB = new ContactInfoDB();
            contactInfoDB.UpdateFamilyInfo(familyContact);

            //Upload Pic to Azure.
            var familyPic = model.FamilyPic;
            if (familyPic != null && familyPic.ContentLength != 0 && !string.IsNullOrWhiteSpace(familyContact.FamilyPicFileName))
            {
                var mediaFileName = familyContact.FamilyPicFileName;
                new AzureFileStorage().UploadFile(mediaFileName, familyPic.InputStream);
            }

            //Send Email.
            GMailDispatcher mailDispatcher = new GMailDispatcher();
            mailDispatcher.SendEditConfirmMsg(familyContact.Email, familyContact.FirstName + " " + familyContact.LastName, familyContact.FamilyContactGuid);

            return RedirectToAction("Detail", new { id = model.FamilyContactGuid });
        }



        private void ValidateModel(ContactInfoViewModel model)
        {
            var mStatus = model.MaritalStatus ?? string.Empty;
            if (mStatus == "M")
            {
                // Check Spouse Info
                if (string.IsNullOrWhiteSpace(model.SpouseFirstName))
                    ModelState.AddModelError("Spouse First Name", "Spouse's First Name is required");
                if (string.IsNullOrWhiteSpace(model.SpouseLastName))
                    ModelState.AddModelError("Spouse Last Name", "Spouse's Last Name is required");
                if (string.IsNullOrWhiteSpace(model.SpouseKovil))
                    ModelState.AddModelError("Spouse Kovil", "Spouse's Kovil at Birth is required");
                if (string.IsNullOrWhiteSpace(model.SpouseKovilPirivu))
                    ModelState.AddModelError("Spouse Kovil Pirivu", "Spouse's Kovil Pirivu at Birth is required");
            }

            var familyPic = model.FamilyPic;
            if (familyPic != null && familyPic.ContentLength != 0)
            {
                var fileExtension = Path.GetExtension(familyPic.FileName).ToLower();
                var index = Array.IndexOf(allowedExtension, fileExtension);
                if (index < 0)
                {
                    ModelState.AddModelError("Family Picture", "Only Images are allowed for Family Picture.");
                }
                model.FamilyPicFileExtn = fileExtension;
            }

            //Validate Kids Information if present
            foreach (var kidsInfo in model.Kids)
            {
                if (string.IsNullOrWhiteSpace(kidsInfo.FirstName) && !(kidsInfo.Age.HasValue) && string.IsNullOrWhiteSpace(kidsInfo.Gender))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(kidsInfo.FirstName))
                    ModelState.AddModelError("Kid First Name", "Kid's First Name is required");
                if (string.IsNullOrWhiteSpace(kidsInfo.Gender))
                    ModelState.AddModelError("Kid Gender", "Kid's Gender is required");
                if (!(kidsInfo.Age.HasValue))
                {
                    ModelState.AddModelError("Kid Age", "Kid's Age is required");
                }
            }
        }

        private FamilyInfoDBModel ProcessViewModeltoDBModel(ContactInfoViewModel model)
        {
            var familyContact = new FamilyInfoDBModel();
            var isNewFamilyContact = string.IsNullOrWhiteSpace(model.FamilyContactGuid);
            familyContact.FamilyContactGuid = isNewFamilyContact ? Guid.NewGuid().ToString("N") : model.FamilyContactGuid;
            familyContact.FirstName = string.IsNullOrWhiteSpace(model.FirstName) ? string.Empty : model.FirstName;
            familyContact.LastName = string.IsNullOrWhiteSpace(model.LastName) ? string.Empty : model.LastName;
            familyContact.Gender = string.IsNullOrWhiteSpace(model.Gender) ? string.Empty : model.Gender;
            familyContact.MaritalStatus = string.IsNullOrWhiteSpace(model.MaritalStatus) ? string.Empty : model.MaritalStatus;

            familyContact.Email = string.IsNullOrWhiteSpace(model.Email) ? string.Empty : model.Email;
            familyContact.HomePhone = string.IsNullOrWhiteSpace(model.HomePhone) ? string.Empty : model.HomePhone;
            familyContact.MobilePhone = string.IsNullOrWhiteSpace(model.MobilePhone) ? null : model.MobilePhone; //Nullable

            familyContact.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address; //Nullable
            familyContact.City = string.IsNullOrWhiteSpace(model.City) ? string.Empty : model.City;
            familyContact.State = string.IsNullOrWhiteSpace(model.State) ? string.Empty : model.State;
            familyContact.ZipCode = string.IsNullOrWhiteSpace(model.ZipCode) ? null : model.ZipCode; //Nullable

            familyContact.Kovil = string.IsNullOrWhiteSpace(model.Kovil) ? string.Empty : model.Kovil;
            familyContact.KovilPirivu = string.IsNullOrWhiteSpace(model.KovilPirivu) ? string.Empty : model.KovilPirivu;
            familyContact.NativePlace = string.IsNullOrWhiteSpace(model.NativePlace) ? string.Empty : model.NativePlace;
            
            familyContact.FamilyPicFileName = string.IsNullOrEmpty(model.FamilyPicFileExtn)? null: string.Concat(familyContact.FamilyContactGuid, model.FamilyPicFileExtn);
            if (familyContact.FamilyPicFileName == null && model.FamilyPicFilePath != null)
            {
                familyContact.FamilyPicFileName = model.FamilyPicFilePath.Substring(model.FamilyPicFilePath.LastIndexOf('/')+1);
            }

            //Attach Spouse
            if (familyContact.MaritalStatus == "M")
            {
                var spouseInfo = new SpouseInfoDBModel
                {
                    FirstName = string.IsNullOrWhiteSpace(model.SpouseFirstName) ? string.Empty : model.SpouseFirstName,
                    LastName = string.IsNullOrWhiteSpace(model.SpouseLastName) ? string.Empty : model.SpouseLastName,
                    Email = string.IsNullOrWhiteSpace(model.SpouseEmail) ? null : model.SpouseEmail,
                    MobilePhone = string.IsNullOrWhiteSpace(model.SpouseMobilePhone) ? null : model.SpouseMobilePhone,
                    Kovil = string.IsNullOrWhiteSpace(model.SpouseKovil) ? string.Empty : model.SpouseKovil,
                    KovilPirivu =
                        string.IsNullOrWhiteSpace(model.SpouseKovilPirivu) ? string.Empty : model.SpouseKovilPirivu,
                    NativePlace =
                        string.IsNullOrWhiteSpace(model.SpouseNativePlace) ? string.Empty : model.SpouseNativePlace
                };
                familyContact.Spouse = spouseInfo;
            }

            familyContact.Kids = new List<KidsInfoDBModel>();

            foreach(KidsViewModel kidsInfo in model.Kids)
            {
                if (!string.IsNullOrWhiteSpace(kidsInfo.FirstName) || kidsInfo.Age.HasValue || !string.IsNullOrWhiteSpace(kidsInfo.Gender))
                {
                    var kid = new KidsInfoDBModel();
                    var isNewKidContact = string.IsNullOrWhiteSpace(kidsInfo.KidsInfoGuid);
                    kid.KidsInfoGuid = isNewKidContact ? Guid.NewGuid().ToString("N") : kidsInfo.KidsInfoGuid;
                    kid.FamilyContactGuid = familyContact.FamilyContactGuid;
                    kid.FirstName = string.IsNullOrWhiteSpace(kidsInfo.FirstName) ? string.Empty : kidsInfo.FirstName;
                    kid.Age = kidsInfo.Age.HasValue ? kidsInfo.Age.Value : 0;
                    kid.Gender = string.IsNullOrWhiteSpace(kidsInfo.Gender) ? string.Empty : kidsInfo.Gender;
                    familyContact.Kids.Add(kid);
                }
            }

            return familyContact;
        }

        private ContactInfoViewModel ProcessDBModeltoViewModel(FamilyInfoDBModel familyContactModel)
        {
            if (familyContactModel == null)
                return null;

            ContactInfoViewModel viewModel = new ContactInfoViewModel
            {
                FamilyContactGuid = familyContactModel.FamilyContactGuid,
                FamilyContactId = familyContactModel.FamilyContactId,
                FirstName = familyContactModel.FirstName,
                LastName = familyContactModel.LastName,
                Gender = familyContactModel.Gender,
                MaritalStatus = familyContactModel.MaritalStatus,
                Email = familyContactModel.Email,
                HomePhone = familyContactModel.HomePhone,
                MobilePhone = familyContactModel.MobilePhone,
                Address = familyContactModel.Address,
                City = familyContactModel.City,
                State = familyContactModel.State,
                ZipCode = familyContactModel.ZipCode,
                Kovil = familyContactModel.Kovil,
                KovilPirivu = familyContactModel.KovilPirivu,
                NativePlace = familyContactModel.NativePlace
            };
            
            if(familyContactModel.Spouse != null && familyContactModel.MaritalStatus == "M")
            {
                viewModel.SpouseFirstName = familyContactModel.Spouse.FirstName;
                viewModel.SpouseLastName = familyContactModel.Spouse.LastName;

                viewModel.SpouseEmail = familyContactModel.Spouse.Email;
                viewModel.SpouseMobilePhone = familyContactModel.Spouse.MobilePhone;

                viewModel.SpouseKovil = familyContactModel.Spouse.Kovil;
                viewModel.SpouseKovilPirivu = familyContactModel.Spouse.KovilPirivu;
                viewModel.SpouseNativePlace = familyContactModel.Spouse.NativePlace;
            }

            if(!string.IsNullOrEmpty(familyContactModel.FamilyPicFileName))
            {
                viewModel.FamilyPicFilePath =
                    $"{AzureFileStorage.ContainerPath}/{familyContactModel.FamilyPicFileName}";
            }
            
            if (familyContactModel.Kids == null || familyContactModel.Kids.Count == 0)
                return viewModel;

            foreach(var kidsDBModel in familyContactModel.Kids)
            {
                var kid = new KidsViewModel
                {
                    KidsInfoGuid = kidsDBModel.KidsInfoGuid,
                    FirstName = kidsDBModel.FirstName,
                    Age = kidsDBModel.Age,
                    Gender = kidsDBModel.Gender
                };
                viewModel.Kids.Add(kid);
            }

            return viewModel;
        }
    }

    public class GMailDispatcher
    {
        private readonly MailAddress _fromAddress;
        private readonly SmtpClient _mailClient;

        public GMailDispatcher()
        {
            _fromAddress = new MailAddress("nsna.northeast@gmail.com", "NSNA NorthEast");
            _mailClient = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = true,
                Credentials = new NetworkCredential("nsna.northeast@gmail.com", "rGK8vbqT2gTC")
            };
        }

        public void SendMessage(string toEmailAddress, string toDisplayName, string subject, Func<string> messageBuilder)
        {
            var message = messageBuilder();
            SendMessage(toEmailAddress, toDisplayName, subject, message);
        }

        public void SendMessage(string toEmailAddress, string toDisplayName, string subject, string message)
        {
            var toMailAddress = new MailAddress(toEmailAddress, toDisplayName);
            using (var mailMessage = new MailMessage(_fromAddress, toMailAddress))
            {
                mailMessage.Subject = subject;
                mailMessage.Body = message;
                mailMessage.IsBodyHtml = true;
                _mailClient.Send(mailMessage);
            }
        }
        
        public void SendCreateConfirmMsg(string toEmailAddress, string name, string familyGuid)
        {
            string subject = "Thanks for registering.";
            var viewLink = "http://nsna-ne.azurewebsites.net/directory/detail/" + familyGuid;
            StringBuilder htmlBuilder = new StringBuilder();
            htmlBuilder.AppendFormat("Hi {0}, <br/>", name);
            htmlBuilder.AppendFormat("Thanks for adding your contact info to NSNA-NE Directory.<br/>You can view the details here - <a href='{0}'>{0}</a><br/>", viewLink );
            htmlBuilder.AppendFormat("For more up to date information, Please visit <a href='{0}'>{0}</a><br/><br/>", "http://nsna-ne.azurewebsites.net/");
            htmlBuilder.Append("Thanks, <br/>NSNA Team");

            SendMessage(toEmailAddress, name, subject, htmlBuilder.ToString());
        }

        public void SendEditConfirmMsg(string toEmailAddress, string name, string familyGuid)
        {
            string subject = "Thanks for updating your info.";
            var viewLink = "http://nsna-ne.azurewebsites.net/directory/detail/" + familyGuid;
            StringBuilder htmlBuilder = new StringBuilder();
            htmlBuilder.AppendFormat("Hi {0}, <br/>", name);
            htmlBuilder.AppendFormat("Thanks for updating your contact info to NSNA-NE Directory.<br/>You can view the details here - <a href='{0}'>{0}</a><br/>", viewLink);
            htmlBuilder.AppendFormat("For more up to date information, Please visit <a href='{0}'>{0}</a><br/><br/>", "http://nsna-ne.azurewebsites.net/");
            htmlBuilder.Append("Thanks, <br/>NSNA Team");

            SendMessage(toEmailAddress, name, subject, htmlBuilder.ToString());
        }
    }

    public class ContactInfoDB
    {
        readonly string _connectionstring;

        public ContactInfoDB()
        {
            _connectionstring = ConfigurationManager.ConnectionStrings["AzureDB"].ConnectionString;
        }

        public ContactInfoDB(string connectionStringName)
        {
            _connectionstring = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
        }

        public void InsertFamilyInfo(FamilyInfoDBModel model)
        {
            using (SqlConnection connection = new SqlConnection(_connectionstring))
            {
                connection.Open();
                InsertOrUpdateFamilyContact(connection, model, () => GetFamilyInfoInsertQuery(model.Spouse != null));
                InsertOrUpdateKidsInfo(connection, model, GetKidsInfoInsertQuery);
                connection.Close();
            }
        }

        public void UpdateFamilyInfo(FamilyInfoDBModel model)
        {
            using (SqlConnection connection = new SqlConnection(_connectionstring))
            {
                connection.Open();
                InsertOrUpdateFamilyContact(connection, model, () => GetFamilyInfoUpdateQuery(model.Spouse != null));
                DeleteKidsInfo(connection, model);
                InsertOrUpdateKidsInfo(connection, model, GetKidsInfoInsertQuery);
                connection.Close();
            }
        }

        private string GetFamilyInfoInsertQuery(bool married)
        {
            string commonfields =
                "FamilyContactGuid, FirstName, Lastname, Gender, Email, HomePhone, MobilePhone, Address, City, State, ZipCode, Kovil, KovilPirivu, NativePlace,MaritalStatus,FamilyPicFileName";
            string commonParams =
                "@FamilyContactGuid, @FirstName, @Lastname, @Gender, @Email, @HomePhone, @MobilePhone, @Address, @City, @State, @ZipCode, @Kovil, @KovilPirivu, @NativePlace, @MaritalStatus,@FamilyPicFileName";
            string spousefields =
                "SpouseFirstName,SpouseLastName,SpouseEmail,SpouseMobilePhone,SpouseKovil,SpouseKovilPirivu,SpouseNativePlace";
            string spouseParams =
                "@SpouseFirstName,@SpouseLastName,@SpouseEmail,@SpouseMobilePhone,@SpouseKovil,@SpouseKovilPirivu,@SpouseNativePlace";

            string query = "INSERT INTO dbo.FamilyContact (" + commonfields + ", CreatedOn, LastModifiedOn) VALUES (" +
                           commonParams + ",@CreatedOn, @LastModifiedOn )";
            if (married)
                query = "INSERT INTO dbo.FamilyContact (" + commonfields + "," + spousefields +
                        ", CreatedOn, LastModifiedOn) VALUES (" + commonParams + "," + spouseParams +
                        ",@CreatedOn,@LastModifiedOn )";
            return query;
        }

        private string GetKidsInfoInsertQuery()
        {
            string kidsFields = "KidsInfoGuid,FamilyContactGuid,FirstName,Age,Gender,CreatedOn,LastModifiedOn";
            string kidsParams = "@KidsInfoGuid,@FamilyContactGuid,@FirstName,@Age,@Gender,@CreatedOn,@LastModifiedOn";
            string query = "INSERT INTO dbo.KidsInfo (" + kidsFields + ") VALUES (" + kidsParams + ")";
            return query;
        }

        private string GetFamilyInfoUpdateQuery(bool married)
        {
            string commonfields =
                "FirstName = @FirstName, Lastname = @Lastname, Gender = @Gender, Email = @Email, HomePhone = @HomePhone, MobilePhone = @MobilePhone, Address = @Address, City = @City, State = @State, ZipCode = @ZipCode, Kovil = @Kovil, KovilPirivu = @KovilPirivu, NativePlace = @NativePlace, MaritalStatus = @MaritalStatus, FamilyPicFileName = @FamilyPicFileName";
            string spousefields =
                "SpouseFirstName = @SpouseFirstName, SpouseLastName = @SpouseLastName, SpouseEmail = @SpouseEmail, SpouseMobilePhone = @SpouseMobilePhone, SpouseKovil = @SpouseKovil, SpouseKovilPirivu = @SpouseKovilPirivu, SpouseNativePlace = @SpouseNativePlace";
            string query = "UPDATE dbo.FamilyContact  SET " + commonfields +
                           ", LastModifiedOn= @LastModifiedOn where FamilyContactGuid = @FamilyContactGuid";
            if (married)
                query = "UPDATE dbo.FamilyContact  SET " + commonfields + "," + spousefields +
                        ", LastModifiedOn= @LastModifiedOn where FamilyContactGuid = @FamilyContactGuid";
            return query;
        }

        private void DeleteKidsInfo(SqlConnection connection, FamilyInfoDBModel model)
        {
            string query = "Delete from KidsInfo where FamilyContactGuid= @FamilyContactGuid";
            using (SqlCommand cmd = new SqlCommand(query, connection))
            {
                cmd.Parameters.Add("@FamilyContactGuid", SqlDbType.VarChar, 128).Value = model.FamilyContactGuid;
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertOrUpdateFamilyContact(SqlConnection connection, FamilyInfoDBModel model,
            Func<string> getQuery)
        {
            string query = getQuery();
            using (SqlCommand cmd = new SqlCommand(query, connection))
            {
                // Primary Contact Value
                cmd.Parameters.Add("@FamilyContactGuid", SqlDbType.VarChar, 128).Value = model.FamilyContactGuid;
                cmd.Parameters.Add("@FirstName", SqlDbType.VarChar, 50).Value = model.FirstName;
                cmd.Parameters.Add("@Lastname", SqlDbType.VarChar, 50).Value = model.LastName;
                cmd.Parameters.Add("@Gender", SqlDbType.VarChar, 1).Value = model.Gender;
                cmd.Parameters.Add("@Email", SqlDbType.VarChar, 128).Value = model.Email;
                cmd.Parameters.Add("@HomePhone", SqlDbType.VarChar, 25).Value = model.HomePhone;
                cmd.Parameters.Add("@MobilePhone", SqlDbType.VarChar, 25).Value = string.IsNullOrEmpty(model.MobilePhone)
                    ? Convert.DBNull
                    : model.MobilePhone;
                cmd.Parameters.Add("@Address", SqlDbType.VarChar, 128).Value = string.IsNullOrEmpty(model.Address)
                    ? Convert.DBNull
                    : model.Address;
                cmd.Parameters.Add("@City", SqlDbType.VarChar, 128).Value = model.City;
                cmd.Parameters.Add("@State", SqlDbType.VarChar, 128).Value = model.State;
                cmd.Parameters.Add("@ZipCode", SqlDbType.VarChar, 25).Value = string.IsNullOrEmpty(model.ZipCode)
                    ? Convert.DBNull
                    : model.ZipCode;
                cmd.Parameters.Add("@Kovil", SqlDbType.VarChar, 50).Value = model.Kovil;
                cmd.Parameters.Add("@KovilPirivu", SqlDbType.VarChar, 50).Value = model.KovilPirivu;
                cmd.Parameters.Add("@NativePlace", SqlDbType.VarChar, 128).Value = model.NativePlace;
                cmd.Parameters.Add("@MaritalStatus", SqlDbType.VarChar, 1).Value = model.MaritalStatus;
                cmd.Parameters.Add("FamilyPicFileName", SqlDbType.VarChar, 128).Value =
                    string.IsNullOrEmpty(model.FamilyPicFileName) ? Convert.DBNull : model.FamilyPicFileName;
                if (model.Spouse != null)
                {
                    var spouse = model.Spouse;
                    cmd.Parameters.Add("@SpouseFirstName", SqlDbType.VarChar, 50).Value = spouse.FirstName;
                    cmd.Parameters.Add("@SpouseLastName", SqlDbType.VarChar, 50).Value = spouse.LastName;

                    cmd.Parameters.Add("@SpouseEmail", SqlDbType.VarChar, 128).Value = string.IsNullOrEmpty(spouse.Email)
                        ? Convert.DBNull
                        : spouse.Email;
                    cmd.Parameters.Add("@SpouseMobilePhone", SqlDbType.VarChar, 25).Value =
                        string.IsNullOrEmpty(spouse.MobilePhone) ? Convert.DBNull : spouse.MobilePhone;

                    cmd.Parameters.Add("@SpouseKovil", SqlDbType.VarChar, 50).Value = spouse.Kovil;
                    cmd.Parameters.Add("@SpouseKovilPirivu", SqlDbType.VarChar, 50).Value = spouse.KovilPirivu;
                    cmd.Parameters.Add("@SpouseNativePlace", SqlDbType.VarChar, 128).Value = spouse.NativePlace;
                }
                cmd.Parameters.Add("@CreatedOn", SqlDbType.DateTime).Value = DateTime.Now.ToLocalTime();
                cmd.Parameters.Add("@LastModifiedOn", SqlDbType.DateTime).Value = DateTime.Now.ToLocalTime();
                cmd.ExecuteNonQuery();
            }
        }

        private void InsertOrUpdateKidsInfo(SqlConnection connection, FamilyInfoDBModel model, Func<string> getQuery)
        {
            foreach (var kidsInfo in model.Kids)
            {
                string query = getQuery();
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    // Primary Contact Value
                    cmd.Parameters.Add("@KidsInfoGuid", SqlDbType.VarChar, 128).Value = kidsInfo.KidsInfoGuid;
                    cmd.Parameters.Add("@FamilyContactGuid", SqlDbType.VarChar, 128).Value = kidsInfo.FamilyContactGuid;
                    cmd.Parameters.Add("@FirstName", SqlDbType.VarChar, 50).Value = kidsInfo.FirstName;
                    cmd.Parameters.Add("@Age", SqlDbType.SmallInt, 50).Value = kidsInfo.Age;
                    cmd.Parameters.Add("@Gender", SqlDbType.VarChar, 1).Value = kidsInfo.Gender;
                    cmd.Parameters.Add("@CreatedOn", SqlDbType.DateTime).Value = DateTime.Now.ToLocalTime();
                    cmd.Parameters.Add("@LastModifiedOn", SqlDbType.DateTime).Value = DateTime.Now.ToLocalTime();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public FamilyInfoDBModel SelectWithKidsInfo(string id)
        {
            FamilyInfoDBModel familyDBModel = new FamilyInfoDBModel();
            using (SqlConnection connection = new SqlConnection(_connectionstring))
            {
                connection.Open();
                var fields =
                    "FamilyContactGuid, FamilyContactId, FirstName, Lastname, Gender, Email, HomePhone, MobilePhone, Address, City, State, ZipCode, Kovil, KovilPirivu, NativePlace,MaritalStatus,FamilyPicFileName";
                var spouseFields =
                    "SpouseFirstName,SpouseLastName,SpouseEmail,SpouseMobilePhone,SpouseKovil,SpouseKovilPirivu,SpouseNativePlace";
                string query = "SELECT " + fields + "," + spouseFields +
                               ", CreatedOn, LastModifiedOn FROM FamilyContact WHERE FamilyContactGuid = @FamilyContactGuid";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.Add("@FamilyContactGuid", SqlDbType.VarChar, 128).Value = id;
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                LoadFamilyInfoDBModel(familyDBModel, reader);
                            }
                        }
                        reader.Close();
                    }
                }
                var kidsField = "FamilyContactGuid,KidsInfoGuid,FirstName,Age,Gender";
                string kidsInfoQuery = "SELECT " + kidsField +
                                       " FROM KidsInfo WHERE FamilyContactGuid = @FamilyContactGuid Order by Age desc";
                using (SqlCommand cmd = new SqlCommand(kidsInfoQuery, connection))
                {
                    cmd.Parameters.Add("@FamilyContactGuid", SqlDbType.VarChar, 128).Value = id;
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            familyDBModel.Kids = new List<KidsInfoDBModel>();
                            while (reader.Read())
                            {
                                var kidsInfoDBModel = new KidsInfoDBModel();
                                LoadKidsInfoDBModel(kidsInfoDBModel,reader);
                                familyDBModel.Kids.Add(kidsInfoDBModel);
                            }
                        }
                        reader.Close();
                    }
                }
                connection.Close();
            }
            return familyDBModel;
        }

        private static void LoadFamilyInfoDBModel(FamilyInfoDBModel familyDBModel, SqlDataReader reader)
        {
            familyDBModel.FamilyContactGuid = reader.GetString(reader.GetOrdinal("FamilyContactGuid"));
            familyDBModel.FamilyContactId = reader.GetInt32(reader.GetOrdinal("FamilyContactId"));
            
            familyDBModel.FirstName = reader.GetString(reader.GetOrdinal("FirstName"));
            familyDBModel.LastName = reader.GetString(reader.GetOrdinal("Lastname"));
            familyDBModel.Gender = reader.GetString(reader.GetOrdinal("Gender"));
            
            familyDBModel.Email = reader.GetString(reader.GetOrdinal("Email"));
            familyDBModel.HomePhone = reader.GetString(reader.GetOrdinal("HomePhone"));
            familyDBModel.MobilePhone = reader.IsDBNull(reader.GetOrdinal("MobilePhone")) ? null : reader.GetString(reader.GetOrdinal("MobilePhone"));

            familyDBModel.Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString(reader.GetOrdinal("Address"));
            familyDBModel.City = reader.GetString(reader.GetOrdinal("City"));
            familyDBModel.State = reader.GetString(reader.GetOrdinal("State"));
            familyDBModel.ZipCode = reader.IsDBNull(reader.GetOrdinal("ZipCode")) ? null : reader.GetString(reader.GetOrdinal("ZipCode"));

            familyDBModel.Kovil = reader.GetString(reader.GetOrdinal("Kovil"));
            familyDBModel.KovilPirivu = reader.GetString(reader.GetOrdinal("KovilPirivu"));
            familyDBModel.NativePlace = reader.GetString(reader.GetOrdinal("NativePlace"));
            familyDBModel.MaritalStatus = reader.GetString(reader.GetOrdinal("MaritalStatus"));
            familyDBModel.FamilyPicFileName = reader.IsDBNull(reader.GetOrdinal("FamilyPicFileName")) ? null : reader.GetString(reader.GetOrdinal("FamilyPicFileName"));

            if (familyDBModel.MaritalStatus == "M")
            {
                familyDBModel.Spouse = new SpouseInfoDBModel
                {
                    FirstName = reader.GetString(reader.GetOrdinal("SpouseFirstName")),
                    LastName = reader.GetString(reader.GetOrdinal("SpouseLastName")),
                    Email =
                        reader.IsDBNull(reader.GetOrdinal("SpouseEmail"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("SpouseEmail")),
                    MobilePhone =
                        reader.IsDBNull(reader.GetOrdinal("SpouseMobilePhone"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("SpouseMobilePhone")),
                    Kovil = reader.GetString(reader.GetOrdinal("SpouseKovil")),
                    KovilPirivu = reader.GetString(reader.GetOrdinal("SpouseKovilPirivu")),
                    NativePlace = reader.GetString(reader.GetOrdinal("SpouseNativePlace"))
                };
            }
            
            familyDBModel.CreatedOn = reader.IsDBNull(reader.GetOrdinal("CreatedOn"))
                ? DateTime.MinValue
                : reader.GetDateTime(reader.GetOrdinal("CreatedOn"));
            familyDBModel.LastModifiedOn = reader.IsDBNull(reader.GetOrdinal("LastModifiedOn"))
                ? DateTime.MinValue
                : reader.GetDateTime(reader.GetOrdinal("LastModifiedOn"));
        }

        private static void LoadKidsInfoDBModel(KidsInfoDBModel kidsInfoDBModel, SqlDataReader reader)
        {
            kidsInfoDBModel.FamilyContactGuid = reader.GetString(0);
            kidsInfoDBModel.KidsInfoGuid = reader.GetString(1);
            kidsInfoDBModel.FirstName = reader.IsDBNull(2) ? null : reader.GetString(2);
            kidsInfoDBModel.Age = reader.GetInt16(3);
            kidsInfoDBModel.Gender = reader.IsDBNull(4) ? null : reader.GetString(4);
        }
        public List<ContactListViewModel> SelectAllforList()
        {
            List<ContactListViewModel> contacts = new List<ContactListViewModel>();
            using (SqlConnection connection = new SqlConnection(_connectionstring))
            {
                connection.Open();
                var fields =
                    "FamilyContactGuid, FirstName, Lastname, Gender, Email, City, State,Kovil, KovilPirivu, NativePlace,MaritalStatus";
                string query = "SELECT " + fields + " FROM FamilyContact Order by CreatedOn desc";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                var contact = new ContactListViewModel
                                {
                                    FamilyContactGuid = reader.GetString(0),
                                    FirstName = reader.GetString(1),
                                    LastName = reader.GetString(2),
                                    Gender = reader.GetString(3),
                                    Email = reader.GetString(4),
                                    City = reader.GetString(5),
                                    State = reader.GetString(6),
                                    Kovil = reader.GetString(7),
                                    KovilPirivu = reader.GetString(8),
                                    NativePlace = reader.GetString(9),
                                    MaritalStatus = reader.GetString(10)
                                };
                                contacts.Add(contact);
                            }
                        }
                    }
                }
            }
            return contacts;
        }

        public List<FamilyInfoDBModel> SelectAllFamilyInfo()
        {
            List<FamilyInfoDBModel> contacts = new List<FamilyInfoDBModel>();
            using (SqlConnection connection = new SqlConnection(_connectionstring))
            {
                connection.Open();
                var fields =
                    "FamilyContactGuid, FamilyContactId, FirstName, Lastname, Gender, Email, HomePhone, MobilePhone, Address, City, State, ZipCode, Kovil, KovilPirivu, NativePlace,MaritalStatus,FamilyPicFileName";
                var spouseFields =
                    "SpouseFirstName,SpouseLastName,SpouseEmail,SpouseMobilePhone,SpouseKovil,SpouseKovilPirivu,SpouseNativePlace";
                string query = "SELECT " + fields + "," + spouseFields +
                               ", CreatedOn, LastModifiedOn FROM FamilyContact order by FirstName, LastName";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                FamilyInfoDBModel familyDBModel = new FamilyInfoDBModel();
                                LoadFamilyInfoDBModel(familyDBModel, reader);
                                contacts.Add(familyDBModel);
                            }
                        }
                    }
                }

                var kidsField = "FamilyContactGuid,KidsInfoGuid,FirstName,Age,Gender";
                string kidsInfoQuery = "SELECT " + kidsField + " FROM KidsInfo order by FamilyContactGuid, Age desc";
                using (SqlCommand cmd = new SqlCommand(kidsInfoQuery, connection))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                var kidsInfo = new KidsInfoDBModel();
                                LoadKidsInfoDBModel(kidsInfo,reader);
                                var familyDBModel = contacts.FirstOrDefault(m => m.FamilyContactGuid == kidsInfo.FamilyContactGuid);
                                if (familyDBModel != null)
                                {
                                    if(familyDBModel.Kids == null)
                                        familyDBModel.Kids = new List<KidsInfoDBModel>();
                                    familyDBModel.Kids.Add(kidsInfo);
                                }
                            }
                        }
                        reader.Close();
                    }
                }
                connection.Close();
            }
            return contacts;
        }
    }

    public class AzureFileStorage
    {
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _blobContainerName;

        public AzureFileStorage()
        {
            _storageAccount = CloudStorageAccount.Parse(
                 CloudConfigurationManager.GetSetting("AzureStorage"));
            _blobContainerName = ConfigurationManager.AppSettings["AzureBlobContainer"];
        }
        
        public static string ContainerPath
        {
            get
            {
                return
                    $"{ConfigurationManager.AppSettings["AzureBlobPath"]}/{ConfigurationManager.AppSettings["AzureBlobContainer"]}";
            }
        }

        public bool UploadFile(string fileName, Stream mediaStream)
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(_blobContainerName);
            if (!container.Exists()) return false;
            CloudBlockBlob imageBlob = container.GetBlockBlobReference(fileName);
            imageBlob.UploadFromStream(mediaStream);
            return true;
        }
    }
}