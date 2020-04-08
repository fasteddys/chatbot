﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using Viber.Bot;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.RegularExpressions;

namespace WebApp
{
    class DialogueFrame
    {
        public enum EnumActivity 
        {
            Unknown,
            ReadMyBiomarkers,
            LoadFile,
            Answer,
            ConversationStart,
            SecretMessage,
            ConversationStartAnswer
        }

        public EnumActivity Activity { get; set; }  //действие пользователя, которое он от нас хочет
        public string Entity { get; set; }  //пока используется только для записи ответа
        public object Tag { get; set; }   //здесь лежит id следующего вопроса или еще что-нибудь

        public DialogueFrame()
        {
            Activity = EnumActivity.Unknown;
            Entity = "";
        }

        public DialogueFrame(EnumActivity activity, string entity = "", object tag = null)
        {
            Activity = activity;
            Entity = string.Copy(entity);
            Tag = tag;
        }

        //viber
        public static DialogueFrame GetDialogueFrame(CallbackData e, HealthBotContext ctx, users dbUser)
        {
            string txt;
            if (e.Message.Type == MessageType.Text)
                txt = ((TextMessage)e.Message).Text;
            else if (e.Message.Type == MessageType.Picture)
                return new DialogueFrame(EnumActivity.LoadFile);//txt = ((PictureMessage)e.Message).Text;
            else return new DialogueFrame(EnumActivity.Unknown);

            return AnylizeMessage(txt, ctx, dbUser);
        }

        //telegram
        public static DialogueFrame GetDialogueFrame(Update e, HealthBotContext ctx, users dbUser)
        {
            string txt;
            if (e.Message.Text == null) txt = e.Message.Caption.ToLower();  //лучше смотреть тип сообщения
            else txt = e.Message.Text.ToLower();
            return AnylizeMessage(txt, ctx, dbUser);
        }

        private static DialogueFrame AnylizeMessage(string txt, HealthBotContext ctx, users dbUser)
        {
            EnumActivity ea;
            string ent = "";
            object tag = null;

            if (txt == "запиши мои показания")
            {
                ea = EnumActivity.ReadMyBiomarkers;
                tag = FindNextQuestion(ctx, dbUser);
            }
            else if (txt == "загрузи файл")
            {
                ea = EnumActivity.LoadFile;
                //доп контекст не нужен?
            }
            else if (txt == "да" || txt == "нет")
            {
                ea = EnumActivity.ConversationStartAnswer;
            }
            else if (txt == "/start")
            {
                ea = EnumActivity.ConversationStart;
            }
            else if (txt == "секретное сообщение")
            {
                ea = EnumActivity.SecretMessage;
            }
            else if (dbUser.id_last_question != null)
            {
                var question = ctx.Questions.Where(t => t.id == dbUser.id_last_question).FirstOrDefault();
                if (question != null)
                    ent = ParseAnswer(txt, question.format, question.splitter);
                if (ent != "")
                {
                    ea = EnumActivity.Answer;
                    tag = FindNextQuestion(ctx, dbUser, dbUser.id_last_question.Value);
                }
                else
                {
                    ea = EnumActivity.Unknown;
                    ent = "Ошибка при вводе показателя. Убедитесь, что Вы ввели все правильно"; 
                };
            }
            else ea = EnumActivity.Unknown;

            return new DialogueFrame(ea, ent, tag);
        }

        private static string ParseAnswer(string txt, string format, string splitter)
        {
            var match = Regex.Match(txt, format);
            string result = "";
            if (match.Groups.Count > 1)
            {
                for (int i = 1; i<match.Groups.Count; i++)
                    result += match.Groups[i] + splitter;
                result = result.Substring(0, result.Length - splitter.Length);  //можно было бы сделать через While
            }
            else result = match.Value;
            return result;
        }

        //следующий вопрос
        private static int? FindNextQuestion(HealthBotContext ctx, users dbUser, int startIndex = -1)
        {
            var q = ctx.Questions
                            .OrderBy(t => t.id)
                            .Where(t => t.id > startIndex)
                            .FirstOrDefault();
            if (q != null)
                return q.id;
            else return null;
        }


        public static string GetNextMessage(DialogueFrame df, users dbUser, HealthBotContext ctx, ref string[] buttons)
        {
            string message = "";
            if (df.Activity == EnumActivity.Answer || df.Activity == EnumActivity.ReadMyBiomarkers)
            {
                if (df.Tag != null)
                {
                    message = ctx.Questions.Find(df.Tag).name;
                    dbUser.id_last_question = (int?)df.Tag;
                }
            }
            else if (df.Activity == EnumActivity.LoadFile)
                message = "Изображение сохранено";
            else if (df.Activity == EnumActivity.ConversationStart)
                message = "Хотите начать разговор?";
            else if (df.Activity == EnumActivity.ConversationStartAnswer)
                message = "Здравствуйте!\nДля записи показаний отправьте мне \"запиши мои показания\"";
            else if (df.Activity == EnumActivity.SecretMessage)
                message = "Секретное сообщение принято";

            //список кнопок (желательно четное число кнопок)
            if (df.Activity == EnumActivity.ConversationStart)
            {
                buttons = new[] { "Да", "Нет" };
            }

            return message;
        }

        public static async void SendNextMessage(DialogueFrame df, HealthBotContext ctx,
                                                            users dbUser, Chat chat, ITelegramBotClient client)
        {
            string message = "";
            ReplyKeyboardMarkup keyboard;
            string[] buttons = null;

            if (df.Activity == EnumActivity.Unknown)
                if (df.Entity != "") message = df.Entity;
                else return;
            else
            {
                message = GetNextMessage(df, dbUser, ctx, ref buttons);
                if (message != "")
                {
                    if (buttons != null)
                    {
                        keyboard = new ReplyKeyboardMarkup
                        {
                            Keyboard = new[] {
                            buttons.Select(t => new Telegram.Bot.Types.ReplyMarkups.KeyboardButton(t))
                        },
                            ResizeKeyboard = true
                        };
                        await client.SendTextMessageAsync(
                          chatId: chat,
                          text: message,
                          replyMarkup: keyboard
                        );
                    }
                    else
                    {
                        await client.SendTextMessageAsync(
                          chatId: chat,
                          text: message,
                          replyMarkup: new ReplyKeyboardRemove()
                        );
                    }
                }
                else dbUser.id_last_question = null;
            }
        }

        public static async void SendNextMessage(DialogueFrame df, HealthBotContext ctx, users dbUser, CallbackData callbackData, IViberBotClient viberBot)
        {
            string message = "";
            Keyboard keyboard;
            string[] buttons = null;

            if (df.Activity == EnumActivity.Unknown)
                if (df.Entity != "") message = df.Entity;
                else return;
            else
            {
                message = GetNextMessage(df, dbUser, ctx, ref buttons);
                if (message != "")
                {
                    dbUser.id_last_question = (int?)df.Tag;
                    if (buttons != null)
                    {
                        keyboard = new Keyboard()
                        {
                            BackgroundColor = "#32C832",
                            Buttons = buttons.Select(t => new Viber.Bot.KeyboardButton() { Text = t }).ToList()
                        };
                        await viberBot.SendKeyboardMessageAsync(new KeyboardMessage
                        {
                            Text = message,
                            Keyboard = keyboard,
                            Receiver = callbackData.Sender.Id,
                            MinApiVersion = callbackData.Message.MinApiVersion,
                            TrackingData = callbackData.Message.TrackingData
                        });
                    }
                    else
                    {
                        await viberBot.SendTextMessageAsync(new TextMessage()
                        {
                            Text = message,
                            Receiver = callbackData.Sender.Id,
                            MinApiVersion = callbackData.Message.MinApiVersion,
                            TrackingData = callbackData.Message.TrackingData
                        });
                    }

                }
                else dbUser.id_last_question = null;
            }
        }
    }
}
