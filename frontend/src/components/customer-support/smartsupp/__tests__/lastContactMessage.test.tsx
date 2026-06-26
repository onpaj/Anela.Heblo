import { lastContactMessage } from "../ConversationDetail";
import { MessageDto } from "../../../../api/hooks/useSmartsupp";

function msg(overrides: Partial<MessageDto>): MessageDto {
  return {
    id: "m",
    authorType: "Visitor",
    content: "text",
    createdAt: "2026-05-15T10:00:00.000Z",
    isFirstReply: false,
    ...overrides,
  };
}

describe("lastContactMessage", () => {
  it("returns the content of the last customer message", () => {
    // Arrange
    const messages = [
      msg({ id: "m1", authorType: "Visitor", content: "Dotaz" }),
      msg({ id: "m2", authorType: "Agent", content: "Odpověď" }),
    ];

    // Act
    const result = lastContactMessage(messages);

    // Assert
    expect(result).toBe("Dotaz");
  });

  it("treats 'contact' authorType as a customer message", () => {
    const messages = [msg({ id: "m1", authorType: "contact", content: "Ahoj" })];

    expect(lastContactMessage(messages)).toBe("Ahoj");
  });

  it("skips a trailing visitor system-event message with null content", () => {
    // SmartSupp emits page-visit events as authorType "Visitor" / subType "system"
    // with null content; they must not be treated as the last customer message.
    const messages = [
      msg({ id: "m1", authorType: "Visitor", content: "To už máme vyzkoušeno" }),
      msg({ id: "m2", authorType: "Agent", content: "Tak to je skvělé!" }),
      msg({ id: "m3", authorType: "Visitor", subType: "system", content: null }),
    ];

    expect(lastContactMessage(messages)).toBe("To už máme vyzkoušeno");
  });

  it("skips visitor messages with whitespace-only content", () => {
    const messages = [
      msg({ id: "m1", authorType: "Visitor", content: "Reálná zpráva" }),
      msg({ id: "m2", authorType: "Visitor", content: "   " }),
    ];

    expect(lastContactMessage(messages)).toBe("Reálná zpráva");
  });

  it("ignores system-subtype messages even when they carry content", () => {
    const messages = [
      msg({ id: "m1", authorType: "Visitor", content: "Skutečný dotaz" }),
      msg({ id: "m2", authorType: "Visitor", subType: "system", content: "navštívil stránku" }),
    ];

    expect(lastContactMessage(messages)).toBe("Skutečný dotaz");
  });

  it("returns null when there is no customer text message", () => {
    const messages = [
      msg({ id: "m1", authorType: "Agent", content: "Dobrý den" }),
      msg({ id: "m2", authorType: "Visitor", subType: "system", content: null }),
    ];

    expect(lastContactMessage(messages)).toBeNull();
  });

  it("returns null for an empty conversation", () => {
    expect(lastContactMessage([])).toBeNull();
  });
});
